﻿using DSharpPlus.Entities;
using Eco.Moose.Tools.Logger;
using Eco.Gameplay.Players;
using Eco.Plugins.DiscordLink.Events;
using Eco.Plugins.DiscordLink.Extensions;
using Eco.Plugins.DiscordLink.Utilities;
using Eco.Shared.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Color = Eco.Shared.Utils.Color;
using Eco.Moose.Utils.Message;

namespace Eco.Plugins.DiscordLink.Modules
{
    public class DiscordChatFeed : FeedModule
    {
        public override string ToString()
        {
            return "Discord Chat Feed";
        }

        protected override DLEventType GetTriggers()
        {
            return DLEventType.DiscordMessageSent;
        }

        protected override async Task<bool> ShouldRun()
        {
            foreach (ChatChannelLink link in DLConfig.Data.ChatChannelLinks)
            {
                if (link.IsValid() && (link.Direction == ChatSyncDirection.DiscordToEco || link.Direction == ChatSyncDirection.Duplex))
                    return true;
            }
            return false;
        }

        protected override async Task UpdateInternal(DiscordLink plugin, DLEventType trigger, params object[] data)
        {
            if (!(data[0] is DiscordMessage message))
                return;

            IEnumerable<ChatChannelLink> chatLinks = DLConfig.ChatLinksForDiscordChannel(message.GetChannel());
            foreach (ChatChannelLink chatLink in chatLinks
                .Where(link => link.Direction == ChatSyncDirection.EcoToDiscord || link.Direction == ChatSyncDirection.Duplex))
            {
                await ForwardMessageToEcoChannel(message, chatLink.EcoChannel);
            }
        }

        private async Task ForwardMessageToEcoChannel(DiscordMessage discordMessage, string ecoChannel)
        {
            Logger.Trace($"Sending Discord message to Eco channel: {ecoChannel}");
            DiscordMember author = await discordMessage.GetChannel().Guild.GetMemberAsync(discordMessage.Author.Id);

            User sender = null;
            LinkedUser linkedUser = UserLinkManager.LinkedUserByDiscordUser(author);
            if (linkedUser != null)
                sender = linkedUser.EcoUser;

            string readableContent = GetReadableContent(discordMessage); 
            string messageContent = readableContent;
            if (sender == null)
            {
                DiscordMember memberAuthor = await discordMessage.Author.LookupMember();
                messageContent = $"{Text.Color(Color.LightBlue, memberAuthor.DisplayName)} {DLConstants.ECO_DISCORDLINK_ICON} {messageContent}";
            }
            else
            {
                messageContent = $"{DLConstants.ECO_DISCORDLINK_ICON} {messageContent}";
            }
            
            var ecoMessage = MessageUtils.FormatMessageForEcoChannel(messageContent, ecoChannel);
            Message.SendChatRaw(sender, ecoMessage);

            // forward message to other servers
            IEnumerable<ChatChannelLink> chatLinks = DLConfig.ChatLinksForEcoChannel(ecoChannel);
            foreach (var chatLink in chatLinks)
            {
                if (chatLink.Guild.Id == discordMessage.Channel.Guild.Id)
                {
                    continue;
                }

                ForwardMessageToDiscordChannel(
                    readableContent, 
                    $"[{GetGuildName(discordMessage.Channel.Guild)}] {discordMessage.Author.Username}",
                    chatLink.Channel,
                    chatLink.UseTimestamp,
                    chatLink.HereAndEveryoneMentionPermission,
                    chatLink.MentionPermissions
                );
            }            
            ++_opsCount;
        }
        
        private string GetGuildName(DiscordGuild guild)
        {
            switch (guild.Id)
            {
                case 662813412413276191:
                    return "BCG";
                case 433039858794233858:
                    return "Comfy";
                case 643910879200411668:
                    return "Test";
                default:
                    return guild.Name;
            }
        }

        private void ForwardMessageToDiscordChannel(string message, string citizenName, DiscordChannel channel, bool useTimestamp, GlobalMentionPermission globalMentionPermission, ChatLinkMentionPermissions chatlinkPermissions)
        {
            Logger.Trace($"Forwarding Discord message to Discord channel {channel.Name}");

            bool allowGlobalMention = globalMentionPermission == GlobalMentionPermission.AnyUser;

            _ = DiscordLink.Obj.Client.SendMessageAsync(channel, MessageUtils.FormatMessageForDiscord(message, channel, citizenName, useTimestamp, allowGlobalMention, chatlinkPermissions));
        }           

        private string GetReadableContent(DiscordMessage message)
        {
            // Substitute Discord standard emojis
            string content = DLConstants.DISCORD_EMOJI_SUBSTITUTION_MAP.Aggregate(message.Content, (current, emojiMapping) => current.Replace(emojiMapping.Key, $"<ecoicon name=\"{emojiMapping.Value}\">"));

            // Substitute custom emojis
            content = MessageUtils.DiscordCustomEmoteRegex.Replace(content, capture =>
            {
                string group1 = capture.Groups[1].Value;
                EmoteIconSubstitution sub = DLConfig.Data.EmoteIconSubstitutions.FirstOrDefault(sub => sub.DiscordEmoteKey.EqualsCaseInsensitive(group1));
                if (sub != null)
                {
                    return $"<ecoicon name=\"{sub.EcoIconKey}\">";
                }
                else
                {
                    return $":{group1}:";
                }
            });

            foreach (var user in message.MentionedUsers)
            {
                if (user == null)
                    continue;

                DiscordMember member = message.GetChannel().Guild.Members.FirstOrDefault(m => m.Value?.Id == user.Id).Value;
                if (member == null)
                    continue;

                string name = $"@{member.DisplayName}";
                content = content.Replace($"<@{user.Id}>", name).Replace($"<@!{user.Id}>", name);
            }
            foreach (var role in message.MentionedRoles)
            {
                if (role == null)
                    continue;

                content = content.Replace($"<@&{role.Id}>", $"@{role.Name}");
            }
            foreach (var channel in message.MentionedChannels)
            {
                if (channel == null)
                    continue;

                content = content.Replace($"<#{channel.Id}>", $"#{channel.Name}");
            }

            if (message.Attachments.Count > 0)
            {
                content += "\nAttachments:";
                foreach (DiscordAttachment attachment in message.Attachments)
                {
                    content += $"\n{attachment.FileName}";
                }
            }

            return content;
        }
    }
}
