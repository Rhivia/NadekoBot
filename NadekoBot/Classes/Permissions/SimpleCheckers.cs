﻿using Discord;
using Discord.Commands;
using System;
using Discord.Commands.Permissions;

namespace NadekoBot.Classes.Permissions {
    public static class SimpleCheckers
    {
        public static ManageRoles CanManageRoles { get; } = new ManageRoles();

        public static Func<Command, User, Channel, bool> OwnerOnly() =>
            (com, user, ch) => NadekoBot.IsOwner(user.Id);

        public static Func<Command, User, Channel, bool> ManageMessages() =>
            (com, user, ch) => NadekoBot.IsOwner(user.Id);

        public class ManageRoles :IPermissionChecker
        {
            public bool CanRun(Command command, User user, Channel channel, out string error) {
                error = string.Empty;
                if(user.ServerPermissions.ManageRoles)
                    return true;
                error = "You do not have a permission to manage roles.";
                return false;
            }
        }
    }
}
