﻿using StardewModdingAPI;

namespace FunnySnek.AntiCheat.Server.Framework
{
    /// <summary>A connected player slot.</summary>
    internal class PlayerSlot
    {
        /// <summary>The metadata for this player.</summary>
        public IMultiplayerPeer Peer { get; set; }

        /// <summary>The number of seconds until the player should be kicked.</summary>
        public int CountDownSeconds { get; set; }

        /// <summary>The message to send when the player is kicked.</summary>
        public string Reason { get; set; }

        /// <summary>The names of the blocked the mod has installed, if any.</summary>
        public string[] BlockedModNames { get; set; }
    }
}
