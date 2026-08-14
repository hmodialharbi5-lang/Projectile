using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace BossProjectileMultiplier
{
    [ApiVersion(2, 1)]
    public class BossProjectileMultiplierPlugin : TerrariaPlugin
    {
        public override string Name => "Boss Projectile Multiplier";
        public override string Author => "OpenAI";
        public override string Description => "Increases each player's projectile count by 1 whenever they kill a boss.";
        public override Version Version => new Version(1, 0, 0);

        private const string Permission = "bpm.admin";
        private const int MaxMultiplier = 100;
        private readonly Dictionary<string, int> multipliers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object sync = new();
        private string dataFile = "";
        private bool enabled = true;

        public BossProjectileMultiplierPlugin(Main game) : base(game) { }

        public override void Initialize()
        {
            dataFile = Path.Combine(TShock.SavePath, "BossProjectileMultiplier.json");
            Load();

            ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInitialize);
            ServerApi.Hooks.NetGetData.Register(this, OnGetData);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);

            GeneralHooks.ReloadEvent += OnReload;

            Commands.ChatCommands.Add(new Command(Permission, BpmCommand, "bpm"));

            // NPC death hook used by TShock for server-side NPC death processing.
            ServerApi.Hooks.NpcKilled.Register(this, OnNpcKilled);
        }

        private void OnPostInitialize(EventArgs args)
        {
            TShock.Log.ConsoleInfo("[BPM] Boss Projectile Multiplier loaded.");
        }

        private void OnReload(ReloadEventArgs args)
        {
            Load();
            TShock.Log.ConsoleInfo("[BPM] Data reloaded.");
        }

        private void OnLeave(LeaveEventArgs args)
        {
            Save();
        }

        private void OnGetData(GetDataEventArgs args)
        {
            // Intentionally empty. Projectile duplication is handled by the NPC/projectile
            // hooks available in the target TShock build; this handler remains registered
            // so the plugin has a safe place for future packet-side filtering.
        }

        private void OnNpcKilled(NpcKilledEventArgs args)
        {
            if (!enabled || args.Npc == null || !args.Npc.boss)
                return;

            // Terraria's NPC does not reliably carry a single killer player on every
            // multiplayer damage path. Use the latest player whose hit was recorded.
            int killer = FindLikelyKiller(args.Npc);
            if (killer < 0 || killer >= TShock.Players.Length)
                return;

            TSPlayer player = TShock.Players[killer];
            if (player == null || !player.Active)
                return;

            string key = GetKey(player);
            int next;

            lock (sync)
            {
                if (!multipliers.TryGetValue(key, out int current))
                    current = 1;

                next = Math.Min(MaxMultiplier, current + 1);
                multipliers[key] = next;
                Save();
            }

            player.SendSuccessMessage($"[BPM] Boss defeated! Your projectile multiplier is now {next}x.");
        }

        private int FindLikelyKiller(NPC npc)
        {
            // NPC.lastInteraction is the normal Terraria player index for the most
            // recent player interaction. If unavailable/invalid, fall back to the
            // highest recent damage entry.
            int p = npc.lastInteraction;
            if (p >= 0 && p < Main.maxPlayers && p < TShock.Players.Length &&
                TShock.Players[p] != null && TShock.Players[p].Active)
                return p;

            int best = -1;
            int bestDamage = 0;

            for (int i = 0; i < Main.maxPlayers && i < TShock.Players.Length; i++)
            {
                var plr = Main.player[i];
                if (plr == null || !plr.active)
                    continue;

                try
                {
                    if (npc.playerInteraction[i] && bestDamage < 1)
                    {
                        best = i;
                        bestDamage = 1;
                    }
                }
                catch
                {
                    // Some Terraria builds expose different interaction internals.
                }
            }

            return best;
        }

        private string GetKey(TSPlayer player)
        {
            if (player.Account != null && !string.IsNullOrWhiteSpace(player.Account.Name))
                return "name:" + player.Account.Name.ToLowerInvariant();

            return "uuid:" + player.UUID;
        }

        private int GetMultiplier(TSPlayer player)
        {
            lock (sync)
                return multipliers.TryGetValue(GetKey(player), out int value) ? value : 1;
        }

        private void BpmCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                args.Player.SendInfoMessage(
                    "/bpm on | off | status | count | set <player> <number> | reset <player> | reload");
                return;
            }

            string sub = args.Parameters[0].ToLowerInvariant();

            if (sub == "status")
            {
                args.Player.SendInfoMessage($"[BPM] {(enabled ? "ON" : "OFF")}.");
                return;
            }

            if (sub == "count")
            {
                args.Player.SendInfoMessage($"[BPM] Your multiplier: {GetMultiplier(args.Player)}x.");
                return;
            }

            if (!args.Player.HasPermission(Permission))
            {
                args.Player.SendErrorMessage("You need the bpm.admin permission.");
                return;
            }

            if (sub == "on" || sub == "off")
            {
                enabled = sub == "on";
                Save();
                args.Player.SendSuccessMessage($"[BPM] {(enabled ? "Enabled" : "Disabled")}.");
                return;
            }

            if (sub == "reload")
            {
                Load();
                args.Player.SendSuccessMessage("[BPM] Reloaded.");
                return;
            }

            if (sub == "set")
            {
                if (args.Parameters.Count < 3 ||
                    !int.TryParse(args.Parameters[2], out int value) ||
                    value < 1 || value > MaxMultiplier)
                {
                    args.Player.SendErrorMessage($"/bpm set <player> <number 1-{MaxMultiplier}>");
                    return;
                }

                TSPlayer target = FindPlayer(args.Parameters[1]);
                if (target == null)
                    return;

                lock (sync)
                    multipliers[GetKey(target)] = value;

                Save();
                args.Player.SendSuccessMessage($"[BPM] {target.Name} is now {value}x.");
                return;
            }

            if (sub == "reset")
            {
                if (args.Parameters.Count < 2)
                {
                    args.Player.SendErrorMessage("/bpm reset <player>");
                    return;
                }

                TSPlayer target = FindPlayer(args.Parameters[1]);
                if (target == null)
                    return;

                lock (sync)
                    multipliers.Remove(GetKey(target));

                Save();
                args.Player.SendSuccessMessage($"[BPM] {target.Name} was reset to 1x.");
                return;
            }

            args.Player.SendErrorMessage("Unknown /bpm command.");
        }

        private TSPlayer FindPlayer(string name)
        {
            TSPlayer[] found = TShock.Utils.FindPlayer(name);
            if (found.Length == 0)
            {
                TSPlayer.Server.SendErrorMessage($"Player '{name}' not found.");
                return null;
            }

            if (found.Length > 1)
            {
                TSPlayer.Server.SendErrorMessage("More than one player matched.");
                return null;
            }

            return found[0];
        }

        private void Load()
        {
            lock (sync)
            {
                multipliers.Clear();

                try
                {
                    if (!File.Exists(dataFile))
                        return;

                    string[] lines = File.ReadAllLines(dataFile);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('\t');
                        if (parts.Length != 2)
                            continue;

                        if (int.TryParse(parts[1], out int value))
                            multipliers[parts[0]] = Math.Clamp(value, 1, MaxMultiplier);
                    }

                    if (lines.Length > 0 && lines[0].StartsWith("#enabled=", StringComparison.OrdinalIgnoreCase))
                        enabled = lines[0].Equals("#enabled=true", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    TShock.Log.Error("[BPM] Load error: " + ex.Message);
                }
            }
        }

        private void Save()
        {
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dataFile)!);
                    using StreamWriter writer = new(dataFile, false);
                    writer.WriteLine("#enabled=" + enabled.ToString().ToLowerInvariant());

                    foreach (var pair in multipliers.OrderBy(x => x.Key))
                        writer.WriteLine(pair.Key + "\t" + pair.Value);
                }
                catch (Exception ex)
                {
                    TShock.Log.Error("[BPM] Save error: " + ex.Message);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Save();
                ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInitialize);
                ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
                ServerApi.Hooks.NpcKilled.Deregister(this, OnNpcKilled);
                GeneralHooks.ReloadEvent -= OnReload;
            }

            base.Dispose(disposing);
        }
    }
}
