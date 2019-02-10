using FunnySnek.AntiCheat.Server.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using System;
using System.Collections.Generic;
using System.Linq;

// Code adapted from FunnySnek's AntiCheat server
namespace FunnySnek.AntiCheat.Server
{
    /// <summary>The entry class called by SMAPI.</summary>
    internal class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The name of the blacklist file on the server.</summary>
        private readonly string WhitelistFileName = "mod-whitelist.json";

        /// <summary>The number of seconds to wait until kicking a player (to make sure they receive the chat the message).</summary>
        private readonly int SecondsUntilKick = 5;

        /// <summary>The connected players.</summary>
        private readonly List<PlayerSlot> PlayersToKick = new List<PlayerSlot>();

        /// <summary>The mod names to allow indexed by mod ID.</summary>
        private readonly List<string> AllowedMods = new List<string>();


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {

            // apply patches
            Patch.PatchAll(this.ModManifest.UniqueID);

            // hook events
            helper.Events.Multiplayer.PeerContextReceived += this.OnPeerContextReceived;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
            helper.Events.GameLoop.SaveLoaded += this.SaveLoaded;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;

            this.Monitor.Log("Mods:");
            foreach (IModInfo mod in helper.ModRegistry.GetAll())
            {
                if (mod.Manifest.Name != this.ModManifest.Name)
                    AllowedMods.Add(mod.Manifest.Name);
            }
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Raised after the player loads a save slot.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void SaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            this.PlayersToKick.Clear();
            if (Context.IsMainPlayer)
            {
                if (!this.AllowedMods.Any())
                    this.SendPublicChat($"Anti-Cheat's {this.WhitelistFileName} file is missing or empty; please reinstall the mod.", error: true);
                else
                    this.SendPublicChat("Anti-Cheat activated.");
            }
        }

        /// <summary>Raised after the mod context for a peer is received. This happens before the game approves the connection, so the player doesn't yet exist in the game.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnPeerContextReceived(object sender, PeerContextReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            // log join
            this.Monitor.Log($"Player joined: {e.Peer.PlayerID}");

            // kick: blocked mods found
            if (e.Peer.HasSmapi)
            {

                foreach (IMultiplayerPeerMod mood in  e.Peer.Mods)
                {
                    this.Monitor.Log($"MOOOOD {mood.Name}");
                }

                string[] excessMods = this
                    .GetExcessMods(e.Peer)
                    .Distinct(StringComparer.InvariantCultureIgnoreCase)
                    .OrderBy(p => p)
                    .ToArray();

                string[] neededMods = this
                    .GetNeededMods(e.Peer)
                    .Distinct(StringComparer.InvariantCultureIgnoreCase)
                    .OrderBy(p => p)
                    .ToArray();

                if (excessMods.Any() || neededMods.Any())
                {
                    this.Monitor.Log($"   Will kick in {this.SecondsUntilKick} seconds: mod mismatch.");
                    this.Monitor.Log($"     Player has excess mods: {string.Join(", ", excessMods)}");
                    this.Monitor.Log($"     Player needs mods: {string.Join(", ", neededMods)}");
                    this.PlayersToKick.Add(new PlayerSlot
                    {
                        Peer = e.Peer,
                        CountDownSeconds = this.SecondsUntilKick,
                        ExcessMods = excessMods,
                        NeededMods = neededMods
                    });
                    return;
                }
            }

            // no issues found
            this.Monitor.Log("   No issues found.");
        }

        /// <summary>Raised after the connection with a peer is severed.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            this.Monitor.Log($"Player quit: {e.Peer.PlayerID}");
        }

        /// <summary>Raised after the game state is updated (≈60 times per second).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsMainPlayer || !Context.IsWorldReady || !e.IsOneSecond)
                return;

            // kick players whose countdowns expired
            foreach (PlayerSlot slot in this.PlayersToKick)
            {
                slot.CountDownSeconds--;
                if (slot.CountDownSeconds < 0)
                {
                    // get player info
                    long playerID = slot.Peer.PlayerID;
                    string name = Game1.getOnlineFarmers().FirstOrDefault(p => p.UniqueMultiplayerID == slot.Peer.PlayerID)?.Name ?? slot.Peer.PlayerID.ToString();

                    // send chat messages
                    this.SendPublicChat($"{name}: you're being kicked by Anti-Cheat. You have different mods", error: true);
                    this.SendDirectMessage(playerID, $"Please remove these mods: {string.Join(", ", slot.ExcessMods)}.");
                    this.SendDirectMessage(playerID, $"Please add these mods: {string.Join(", ", slot.NeededMods)}.");
                    if (slot.ExcessMods.Any())
                    {
                        this.SendPublicChat($"{name} Could not connect, they had excess mods: {string.Join(", ", slot.ExcessMods)}");
                    }
                    if (slot.NeededMods.Any())
                    {
                        this.SendPublicChat($"{name} Could not connect, they did not have these mods: {string.Join(", ", slot.NeededMods)}");
                    }

                    // kick player
                    this.KickPlayer(playerID);
                }
            }
            this.PlayersToKick.RemoveAll(p => p.CountDownSeconds < 0);
        }
        


        // Returns a list of mods that the server has which the connecting player does not have
        private IEnumerable<string> GetNeededMods(IMultiplayerPeer peer)
        {
            foreach (var modName in this.AllowedMods)
            {
                if (!peer.Mods.Select(mod => mod.Name).Contains(modName))
                {
                    yield return modName;
                }
            }
        }


        // Returns a list of mods that the connecting player has which are not on the server.
        private IEnumerable<string> GetExcessMods(IMultiplayerPeer peer)
        {
            foreach (var mod in peer.Mods)
            {
                if (!this.AllowedMods.Contains(mod.Name))
                {
                    yield return mod.Name;
                }
            }
        }

        /// <summary>Send a chat message to all players.</summary>
        /// <param name="text">The chat text to send.</param>
        /// <param name="error">Whether to format the text as an error.</param>
        private void SendPublicChat(string text, bool error = false)
        {
            // format text
            if (error)
            {
                Game1.chatBox.activate();
                Game1.chatBox.setText("/color red");
                Game1.chatBox.chatBox.RecieveCommandInput('\r');
                Game1.chatBox.setText("/color white");
            }

            // send chat message
            // (Bypass Game1.chatBox.setText which doesn't handle long text well)
            Game1.chatBox.activate();
            Game1.chatBox.chatBox.reset();
            Game1.chatBox.chatBox.finalText.Add(new ChatSnippet(text, LocalizedContentManager.LanguageCode.en));
            Game1.chatBox.chatBox.updateWidth();
            Game1.chatBox.chatBox.RecieveCommandInput('\r');
        }

        /// <summary>Send a private message to a specified player.</summary>
        /// <param name="playerID">The player ID.</param>
        /// <param name="text">The text to send.</param>
        private void SendDirectMessage(long playerID, string text)
        {
            Game1.server.sendMessage(playerID, Multiplayer.chatMessage, Game1.player, this.Helper.Content.CurrentLocaleConstant, text);
        }

        /// <summary>Kick a player from the server.</summary>
        /// <param name="playerID">The unique player ID.</param>
        private void KickPlayer(long playerID)
        {
            // kick player
            try
            {
                Game1.server.sendMessage(playerID, new OutgoingMessage(Multiplayer.disconnecting, playerID));
            }
            catch { /* ignore error if we can't connect to the player */ }
            Game1.server.playerDisconnected(playerID);
            Game1.otherFarmers.Remove(playerID);
        }
    }
}
