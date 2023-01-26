﻿using DSharpPlus.Entities;
using Eco.Gameplay.Systems.Chat;
using Eco.Plugins.DiscordLink.Events;
using Eco.Plugins.DiscordLink.Extensions;
using Eco.Plugins.DiscordLink.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Eco.Gameplay.GameActions;

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
                await ForwardMessageToEcoChannel(plugin, message, chatLink.EcoChannel);
            }
        }

        private async Task ForwardMessageToEcoChannel(DiscordLink plugin, DiscordMessage message, string ecoChannel)
        {
            Logger.DebugVerbose($"Sending Discord message to Eco channel: {ecoChannel}");
            var ecoMessage = await MessageUtils.FormatMessageForEco(message, ecoChannel);
            EcoUtils.SendChatRaw(ecoMessage);
            
            // forward message to other servers
            var forwardMessage = MessageUtils.GetReadableContent(message);
            IEnumerable<ChatChannelLink> chatLinks = DLConfig.ChatLinksForEcoChannel(ecoChannel);
            foreach (var chatLink in chatLinks)
            {
                if (chatLink.Guild.Id == message.Channel.Guild.Id)
                {
                    continue;
                }
                
                ForwardMessageToDiscordChannel(
                    forwardMessage, 
                    $"[{GetGuildName(message.Channel.Guild)}] {message.Author.Username}",
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
            Logger.DebugVerbose($"Forwarding Discord message to Discord channel {channel.Name}");

            bool allowGlobalMention = globalMentionPermission == GlobalMentionPermission.AnyUser;

            _ = DiscordLink.Obj.Client.SendMessageAsync(channel, MessageUtils.FormatMessageForDiscord(message, channel, citizenName, useTimestamp, allowGlobalMention, chatlinkPermissions));
        }        
    }
}
