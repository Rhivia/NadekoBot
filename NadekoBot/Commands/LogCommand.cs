﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Timers;
using System.Threading.Tasks;
using Discord.Commands;
using Discord;
using NadekoBot.Classes.Permissions;

namespace NadekoBot.Commands {
    internal class LogCommand : IDiscordCommand {

        private class Repeater {
            public readonly Timer MessageTimer = new Timer();
            public Channel ReChannel { get; set; }
            public string ReMessage { get; set; }

            public Repeater() {
                MessageTimer.Elapsed += async (s, e) => {
                    try {
                        var ch = ReChannel;
                        var msg = ReMessage;
                        if (ch != null && !string.IsNullOrWhiteSpace(msg))
                            await ch.SendMessage(msg);
                    } catch { }
                };
            }
        }

        private readonly ConcurrentDictionary<Server, Channel> logs = new ConcurrentDictionary<Server, Channel>();
        private readonly ConcurrentDictionary<Server, Channel> loggingPresences = new ConcurrentDictionary<Server, Channel>();
        private readonly ConcurrentDictionary<Channel, Channel> voiceChannelLog = new ConcurrentDictionary<Channel, Channel>();

        private readonly ConcurrentDictionary<Server, Repeater> repeaters = new ConcurrentDictionary<Server, Repeater>();

        public LogCommand() {
            NadekoBot.Client.MessageReceived += MsgRecivd;
            NadekoBot.Client.MessageDeleted += MsgDltd;
            NadekoBot.Client.MessageUpdated += MsgUpdtd;
            NadekoBot.Client.UserUpdated += UsrUpdtd;

            if (NadekoBot.Config.SendPrivateMessageOnMention)
                NadekoBot.Client.MessageReceived += async (s, e) => {
                    try {
                        if (e.Channel.IsPrivate)
                            return;
                        var usr = e.Message.MentionedUsers.FirstOrDefault(u => u != e.User);
                        if (usr?.Status != UserStatus.Offline)
                            return;
                        await e.Channel.SendMessage($"User `{usr.Name}` is offline. PM sent.");
                        await usr.SendMessage(
                            $"User `{e.User.Name}` mentioned you on " +
                            $"`{e.Server.Name}` server while you were offline.\n" +
                            $"`Message:` {e.Message.Text}");

                    } catch { }
                };
        }

        public Func<CommandEventArgs, Task> DoFunc() => async e => {
            if (!NadekoBot.IsOwner(e.User.Id) ||
                          !e.User.ServerPermissions.ManageServer)
                return;
            Channel ch;
            if (!logs.TryRemove(e.Server, out ch)) {
                logs.TryAdd(e.Server, e.Channel);
                await e.Channel.SendMessage($"**I WILL BEGIN LOGGING SERVER ACTIVITY IN THIS CHANNEL**");
                return;
            }

            await e.Channel.SendMessage($"**NO LONGER LOGGING IN {ch.Mention} CHANNEL**");
        };

        private async void MsgRecivd(object sender, MessageEventArgs e) {
            try {
                if (e.Server == null || e.Channel.IsPrivate || e.User.Id == NadekoBot.Client.CurrentUser.Id)
                    return;
                Channel ch;
                if (!logs.TryGetValue(e.Server, out ch) || e.Channel == ch)
                    return;
                await ch.SendMessage($"`Type:` **Message received** `Time:` **{DateTime.Now}** `Channel:` **{e.Channel.Name}**\n`{e.User}:` {e.Message.Text}");
            } catch { }
        }
        private async void MsgDltd(object sender, MessageEventArgs e) {
            try {
                if (e.Server == null || e.Channel.IsPrivate || e.User.Id == NadekoBot.Client.CurrentUser.Id)
                    return;
                Channel ch;
                if (!logs.TryGetValue(e.Server, out ch) || e.Channel == ch)
                    return;
                await ch.SendMessage($"`Type:` **Message deleted** `Time:` **{DateTime.Now}** `Channel:` **{e.Channel.Name}**\n`{e.User}:` {e.Message.Text}");
            } catch { }
        }
        private async void MsgUpdtd(object sender, MessageUpdatedEventArgs e) {
            try {
                if (e.Server == null || e.Channel.IsPrivate || e.User.Id == NadekoBot.Client.CurrentUser.Id)
                    return;
                Channel ch;
                if (!logs.TryGetValue(e.Server, out ch) || e.Channel == ch)
                    return;
                await ch.SendMessage($"`Type:` **Message updated** `Time:` **{DateTime.Now}** `Channel:` **{e.Channel.Name}**\n**BEFORE**: `{e.User}:` {e.Before.Text}\n---------------\n**AFTER**: `{e.User}:` {e.After.Text}");
            } catch { }
        }
        private async void UsrUpdtd(object sender, UserUpdatedEventArgs e) {
            try {
                Channel ch;
                if (loggingPresences.TryGetValue(e.Server, out ch))
                    if (e.Before.Status != e.After.Status) {
                        await ch.SendMessage($"**{e.Before.Name}** is now **{e.After.Status}**.");
                    }
            } catch { }

            try {
                if (e.Before.VoiceChannel != null && voiceChannelLog.ContainsKey(e.Before.VoiceChannel)) {
                    if (e.After.VoiceChannel != e.Before.VoiceChannel)
                        await voiceChannelLog[e.Before.VoiceChannel].SendMessage($"🎼`{e.Before.Name} has left the` {e.Before.VoiceChannel.Mention} `voice channel.`");
                }
                if (e.After.VoiceChannel != null && voiceChannelLog.ContainsKey(e.After.VoiceChannel)) {
                    if (e.After.VoiceChannel != e.Before.VoiceChannel)
                        await voiceChannelLog[e.After.VoiceChannel].SendMessage($"🎼`{e.After.Name} has joined the`{e.After.VoiceChannel.Mention} `voice channel.`");
                }
            } catch { }

            try {
                Channel ch;
                if (!logs.TryGetValue(e.Server, out ch))
                    return;
                string str = $"`Type:` **User updated** `Time:` **{DateTime.Now}** `User:` **{e.Before.Name}**\n";
                if (e.Before.Name != e.After.Name)
                    str += $"`New name:` **{e.After.Name}**";
                else if (e.Before.AvatarUrl != e.After.AvatarUrl)
                    str += $"`New Avatar:` {e.After.AvatarUrl}";
                else if (e.Before.Status != e.After.Status)
                    str += $"Status `{e.Before.Status}` -> `{e.After.Status}`";
                else
                    return;
                await ch.SendMessage(str);
            } catch { }
        }

        public void Init(CommandGroupBuilder cgb) {
            cgb.CreateCommand(".repeat")
                .Description("Repeat a message every X minutes. If no parameters are specified, repeat is disabled. Requires manage messages.")
                .Parameter("minutes", ParameterType.Optional)
                .Parameter("msg", ParameterType.Unparsed)
                .AddCheck(SimpleCheckers.ManageMessages())
                .Do(async e => {
                    var minutesStr = e.GetArg("minutes");
                    var msg = e.GetArg("msg");

                    // if both null, disable
                    if (string.IsNullOrWhiteSpace(msg) && string.IsNullOrWhiteSpace(minutesStr)) {
                        await e.Channel.SendMessage("Repeating disabled");
                        Repeater rep;
                        if (repeaters.TryGetValue(e.Server, out rep))
                            rep.MessageTimer.Stop();
                        return;
                    }
                    int minutes;
                    if (!int.TryParse(minutesStr, out minutes) || minutes < 1 || minutes > 720) {
                        await e.Channel.SendMessage("Invalid value");
                        return;
                    }

                    var repeater = repeaters.GetOrAdd(e.Server, s => new Repeater());

                    repeater.ReChannel = e.Channel;
                    repeater.MessageTimer.Interval = minutes * 60 * 1000;

                    if (!string.IsNullOrWhiteSpace(msg))
                        repeater.ReMessage = msg;

                    repeater.MessageTimer.Stop();
                    repeater.MessageTimer.Start();

                    await e.Channel.SendMessage(String.Format("👌 Repeating `{0}` every " +
                                                              "**{1}** minutes on {2} channel.",
                                                              repeater.ReMessage, minutes, repeater.ReChannel));
                });

            cgb.CreateCommand(".logserver")
                  .Description("Toggles logging in this channel. Logs every message sent/deleted/edited on the server. BOT OWNER ONLY. SERVER OWNER ONLY.")
                  .Do(DoFunc());

            cgb.CreateCommand(".userpresence")
                  .Description("Starts logging to this channel when someone from the server goes online/offline/idle. BOT OWNER ONLY. SERVER OWNER ONLY.")
                  .Do(async e => {
                      if (!NadekoBot.IsOwner(e.User.Id) ||
                          !e.User.ServerPermissions.ManageServer)
                          return;
                      Channel ch;
                      if (!loggingPresences.TryRemove(e.Server, out ch)) {
                          loggingPresences.TryAdd(e.Server, e.Channel);
                          await e.Channel.SendMessage($"**User presence notifications enabled.**");
                          return;
                      }

                      await e.Channel.SendMessage($"**User presence notifications disabled.**");
                  });

            cgb.CreateCommand(".voicepresence")
                  .Description("Toggles logging to this channel whenever someone joins or leaves a voice channel you are in right now. BOT OWNER ONLY. SERVER OWNER ONLY.")
                  .Parameter("all", ParameterType.Optional)
                  .Do(async e => {
                      if (!NadekoBot.IsOwner(e.User.Id) ||
                          !e.User.ServerPermissions.ManageServer)
                          return;

                      if (e.GetArg("all")?.ToLower() == "all") {
                          foreach (var voiceChannel in e.Server.VoiceChannels) {
                              voiceChannelLog.TryAdd(voiceChannel, e.Channel);
                          }
                          await e.Channel.SendMessage("Started logging user presence for **ALL** voice channels!");
                          return;
                      }

                      if (e.User.VoiceChannel == null) {
                          await e.Channel.SendMessage("💢 You are not in a voice channel right now. If you are, please rejoin it.");
                          return;
                      }
                      Channel throwaway;
                      if (!voiceChannelLog.TryRemove(e.User.VoiceChannel, out throwaway)) {
                          voiceChannelLog.TryAdd(e.User.VoiceChannel, e.Channel);
                          await e.Channel.SendMessage($"`Logging user updates for` {e.User.VoiceChannel.Mention} `voice channel.`");
                      } else
                          await e.Channel.SendMessage($"`Stopped logging user updates for` {e.User.VoiceChannel.Mention} `voice channel.`");
                  });
        }
    }
}
