using StardewModdingAPI;

namespace FunnySnek.AntiCheat.Server.Framework
{
    /// <summary>A connected player slot.</summary>
    internal class PlayerSlot
    {
        /// <summary>The metadata for this player.</summary>
        public IMultiplayerPeer Peer { get; set; }

        /// <summary>The number of seconds until the player should be kicked.</summary>
        public int CountDownSeconds { get; set; }
        
        public string[] ExcessMods { get; set; }

        public string[] NeededMods { get; set; }
    }
}
