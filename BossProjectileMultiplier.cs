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
        public override string Description =>
            "Increases a player's projectile multiplier whenever they defeat a boss.";

        public override Version Version => new Version(1, 0, 0);

        private const string Permission = "bpm.admin";
        private const int DefaultMultiplier = 1;
        private const int MaximumMultiplier = 999;

        private readonly Dictionary<string, int> multipliers =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly object sync = new object();

        private string DataFile =>
            Path.Combine(TShock.SavePath, "BossProjectileMultiplier.txt");

        private bool Enabled = true;

        public BossProjectileMultiplierPlugin(Main game)
            : base(game)
        {
        }

        public override void Initialize()
        {
            LoadData();

            GeneralHooks.ReloadEvent += OnReload;

            ServerApi.Hooks.ServerLeave.Register(
                this,
                OnPlayerLeave
            );

            Commands.ChatCommands.Add(
                new Command(
                    Permission,
                    BpmCommand,
                    "bpm"
                )
            );

            TShock.Log.ConsoleInfo(
                "[BossProjectileMultiplier] Loaded."
            );
        }

        private void OnReload(ReloadEventArgs args)
        {
            LoadData();

            args.Player.SendSuccessMessage(
                "[BPM] Data reloaded."
            );
        }

        private void OnPlayerLeave(LeaveEventArgs args)
        {
            SaveData();
        }

        private string GetPlayerKey(TSPlayer player)
        {
            if (player.Account != null &&
                !string.IsNullOrWhiteSpace(player.Account.Name))
            {
                return "account:" +
                       player.Account.Name.ToLowerInvariant();
            }

            return "uuid:" + player.UUID;
        }

        private int GetMultiplier(TSPlayer player)
        {
            string key = GetPlayerKey(player);

            lock (sync)
            {
                if (multipliers.TryGetValue(key, out int value))
                    return value;
            }

            return DefaultMultiplier;
        }

        private void SetMultiplier(TSPlayer player, int value)
        {
            value = Math.Clamp(
                value,
                DefaultMultiplier,
                MaximumMultiplier
            );

            lock (sync)
            {
                multipliers[GetPlayerKey(player)] = value;
            }

            SaveData();
        }

        private void AddBossKill(TSPlayer player)
        {
            if (!Enabled || player == null || !player.Active)
                return;

            int oldValue = GetMultiplier(player);

            int newValue = Math.Min(
                MaximumMultiplier,
                oldValue + 1
            );

            SetMultiplier(player, newValue);

            player.SendSuccessMessage(
                $"[BPM] Boss defeated! " +
                $"Your projectile count is now {newValue}."
            );
        }

        private int GetPlayerProjectileCount(TSPlayer player)
        {
            return GetMultiplier(player);
        }

        private void BpmCommand(CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                ShowHelp(args.Player);
                return;
            }

            string subcommand =
                args.Parameters[0].ToLowerInvariant();

            switch (subcommand)
            {
                case "on":
                    if (!RequireAdmin(args))
                        return;

                    Enabled = true;
                    SaveData();

                    args.Player.SendSuccessMessage(
                        "[BPM] Enabled."
                    );
                    break;

                case "off":
                    if (!RequireAdmin(args))
                        return;

                    Enabled = false;
                    SaveData();

                    args.Player.SendSuccessMessage(
                        "[BPM] Disabled. Player progress was NOT reset."
                    );
                    break;

                case "status":
                    args.Player.SendInfoMessage(
                        $"[BPM] Status: {(Enabled ? "ON" : "OFF")}"
                    );

                    args.Player.SendInfoMessage(
                        $"[BPM] Your multiplier: " +
                        $"{GetMultiplier(args.Player)}"
                    );
                    break;

                case "count":
                    args.Player.SendInfoMessage(
                        $"[BPM] Your projectile count: " +
                        $"{GetMultiplier(args.Player)}"
                    );
                    break;

                case "set":
                    SetCommand(args);
                    break;

                case "reset":
                    ResetCommand(args);
                    break;

                case "reload":
                    if (!RequireAdmin(args))
                        return;

                    LoadData();

                    args.Player.SendSuccessMessage(
                        "[BPM] Reloaded."
                    );
                    break;

                default:
                    ShowHelp(args.Player);
                    break;
            }
        }

        private bool RequireAdmin(CommandArgs args)
        {
            if (args.Player.HasPermission(Permission))
                return true;

            args.Player.SendErrorMessage(
                "You need the bpm.admin permission."
            );

            return false;
        }

        private void SetCommand(CommandArgs args)
        {
            if (!RequireAdmin(args))
                return;

            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage(
                    "/bpm set <player> <number>"
                );
                return;
            }

            if (!int.TryParse(
                    args.Parameters[2],
                    out int amount))
            {
                args.Player.SendErrorMessage(
                    "The number must be an integer."
                );
                return;
            }

            if (amount < DefaultMultiplier ||
                amount > MaximumMultiplier)
            {
                args.Player.SendErrorMessage(
                    $"Number must be between " +
                    $"{DefaultMultiplier} and {MaximumMultiplier}."
                );
                return;
            }

            TSPlayer? target =
                FindPlayer(args.Parameters[1]);

            if (target == null)
                return;

            SetMultiplier(target, amount);

            args.Player.SendSuccessMessage(
                $"[BPM] {target.Name} is now at {amount}."
            );
        }

        private void ResetCommand(CommandArgs args)
        {
            if (!RequireAdmin(args))
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage(
                    "/bpm reset <player>"
                );
                return;
            }

            TSPlayer? target =
                FindPlayer(args.Parameters[1]);

            if (target == null)
                return;

            SetMultiplier(
                target,
                DefaultMultiplier
            );

            args.Player.SendSuccessMessage(
                $"[BPM] {target.Name} was reset to 1."
            );
        }

        private TSPlayer? FindPlayer(string name)
        {
            IEnumerable<TSPlayer> matches =
                TShock.Players
                    .Where(p =>
                        p != null &&
                        p.Active &&
                        (
                            p.Name.Equals(
                                name,
                                StringComparison.OrdinalIgnoreCase
                            )
                            ||
                            p.Name.IndexOf(
                                name,
                                StringComparison.OrdinalIgnoreCase
                            ) >= 0
                        ));

            TSPlayer[] players = matches.ToArray();

            if (players.Length == 0)
            {
                TSPlayer.All.SendErrorMessage(
                    $"Player '{name}' was not found."
                );

                return null;
            }

            if (players.Length > 1)
            {
                TSPlayer.All.SendErrorMessage(
                    "Multiple players matched that name."
                );

                return null;
            }

            return players[0];
        }

        private void ShowHelp(TSPlayer player)
        {
            player.SendInfoMessage(
                "[BPM] Commands:"
            );

            player.SendInfoMessage(
                "/bpm count"
            );

            player.SendInfoMessage(
                "/bpm status"
            );

            if (player.HasPermission(Permission))
            {
                player.SendInfoMessage(
                    "/bpm on"
                );

                player.SendInfoMessage(
                    "/bpm off"
                );

                player.SendInfoMessage(
                    "/bpm set <player> <number>"
                );

                player.SendInfoMessage(
                    "/bpm reset <player>"
                );

                player.SendInfoMessage(
                    "/bpm reload"
                );
            }
        }

        private void LoadData()
        {
            lock (sync)
            {
                multipliers.Clear();

                if (!File.Exists(DataFile))
                    return;

                try
                {
                    foreach (
                        string line
                        in File.ReadAllLines(DataFile))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.StartsWith(
                            "#enabled=",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            string value =
                                line.Substring(9);

                            Enabled =
                                bool.TryParse(
                                    value,
                                    out bool enabled)
                                    ? enabled
                                    : true;

                            continue;
                        }

                        string[] parts =
                            line.Split('\t');

                        if (parts.Length != 2)
                            continue;

                        if (!int.TryParse(
                                parts[1],
                                out int multiplier))
                            continue;

                        multiplier = Math.Clamp(
                            multiplier,
                            DefaultMultiplier,
                            MaximumMultiplier
                        );

                        multipliers[parts[0]] =
                            multiplier;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(
                        "[BPM] Failed to load data: " +
                        ex
                    );
                }
            }
        }

        private void SaveData()
        {
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(
                        TShock.SavePath
                    );

                    using StreamWriter writer =
                        new StreamWriter(
                            DataFile,
                            false
                        );

                    writer.WriteLine(
                        "#enabled=" +
                        Enabled.ToString().ToLowerInvariant()
                    );

                    foreach (
                        KeyValuePair<string, int> pair
                        in multipliers.OrderBy(
                            x => x.Key))
                    {
                        writer.WriteLine(
                            pair.Key +
                            "\t" +
                            pair.Value
                        );
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(
                        "[BPM] Failed to save data: " +
                        ex
                    );
                }
            }
        }

        protected override void Dispose(
            bool disposing)
        {
            if (disposing)
            {
                SaveData();

                GeneralHooks.ReloadEvent -=
                    OnReload;

                ServerApi.Hooks.ServerLeave.Deregister(
                    this,
                    OnPlayerLeave
                );
            }

            base.Dispose(disposing);
        }
    }
}
