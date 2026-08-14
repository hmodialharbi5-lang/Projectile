using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace BossProjectileMultiplier
{
    [ApiVersion(2, 1)]
    public class BossProjectileMultiplierPlugin : TerrariaPlugin
    {
        public override string Name =>
            "Boss Projectile Multiplier";

        public override string Author =>
            "OpenAI";

        public override string Description =>
            "Multiplies player projectiles based on the player's BPM multiplier.";

        public override Version Version =>
            new Version(3, 0, 0);

        private const string Permission = "bpm.admin";

        private const int DefaultMultiplier = 1;
        private const int MaximumMultiplier = 999;

        // Distance between grid slots.
        private const float GridSpacing = 18f;

        private readonly Dictionary<string, int> multipliers =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        private readonly object sync =
            new object();

        private bool Enabled = true;

        private string DataFile =>
            Path.Combine(
                TShock.SavePath,
                "BossProjectileMultiplier.txt");

        // Reflection cache for Terraria.Projectile.NewProjectile.
        private MethodInfo? newProjectileMethod;

        public BossProjectileMultiplierPlugin(Main game)
            : base(game)
        {
        }

        public override void Initialize()
        {
            LoadData();

            FindNewProjectileMethod();

            GeneralHooks.ReloadEvent +=
                OnReload;

            ServerApi.Hooks.ServerLeave.Register(
                this,
                OnPlayerLeave);

            // TShock 6.1.0 HandlerList registration.
            GetDataHandlers.NewProjectile.Register(
                OnNewProjectile);

            Commands.ChatCommands.Add(
                new Command(
                    Permission,
                    BpmCommand,
                    "bpm"));

            TShock.Log.ConsoleInfo(
                "[BPM] Boss Projectile Multiplier loaded.");
        }

        private void FindNewProjectileMethod()
        {
            newProjectileMethod =
                typeof(Projectile)
                    .GetMethods(
                        BindingFlags.Public |
                        BindingFlags.Static)
                    .Where(m =>
                        m.Name == "NewProjectile")
                    .Where(m =>
                    {
                        ParameterInfo[] p =
                            m.GetParameters();

                        return
                            p.Length >= 10 &&
                            p.Length <= 12;
                    })
                    .OrderByDescending(m =>
                        m.GetParameters().Length)
                    .FirstOrDefault();

            if (newProjectileMethod == null)
            {
                TShock.Log.ConsoleError(
                    "[BPM] Could not find Terraria.Projectile.NewProjectile."
                );
            }
        }

        private void OnReload(
            ReloadEventArgs args)
        {
            LoadData();

            args.Player.SendSuccessMessage(
                "[BPM] Data reloaded.");
        }

        private void OnPlayerLeave(
            LeaveEventArgs args)
        {
            SaveData();
        }

        // =========================================================
        // PROJECTILE MULTIPLICATION
        // =========================================================

        private void OnNewProjectile(
            object sender,
            GetDataHandlers.NewProjectileEventArgs args)
        {
            if (!Enabled)
                return;

            if (args == null)
                return;

            TSPlayer player =
                args.Player;

            if (player == null ||
                !player.Active)
            {
                return;
            }

            // Only multiply projectiles actually owned
            // by the player who sent the packet.
            if (args.Owner != player.Index)
                return;

            int multiplier =
                GetMultiplier(player);

            if (multiplier <= 1)
                return;

            int extra =
                multiplier - 1;

            if (extra <= 0)
                return;

            SpawnFormation(
                args,
                multiplier);
        }

        private void SpawnFormation(
            GetDataHandlers.NewProjectileEventArgs args,
            int totalProjectiles)
        {
            Vector2 velocity =
                args.Velocity;

            if (velocity.LengthSquared() < 0.0001f)
                return;

            if (newProjectileMethod == null)
                return;

            Vector2 forward =
                velocity;

            forward.Normalize();

            Vector2 sideways =
                new Vector2(
                    -forward.Y,
                    forward.X);

            // Keep at most 5 rows.
            //
            // 1  -> 1 x 1
            // 2  -> 2 x 1
            // 6  -> 2 x 3
            // 10 -> 2 x 5
            // 11 -> 3 x 4
            // 20 -> 4 x 5
            // 25 -> 5 x 5
            // 30 -> 6 x 5
            int columns =
                (int)Math.Ceiling(
                    totalProjectiles / 5.0);

            if (columns < 1)
                columns = 1;

            int rows =
                (int)Math.Ceiling(
                    totalProjectiles /
                    (double)columns);

            if (rows < 1)
                rows = 1;

            // The original projectile occupies
            // the center-ish slot.
            int originalColumn =
                (columns - 1) / 2;

            int originalRow =
                (rows - 1) / 2;

            int spawned =
                0;

            // Create totalProjectiles - 1 copies.
            for (int row = 0;
                 row < rows;
                 row++)
            {
                for (int column = 0;
                     column < columns;
                     column++)
                {
                    // Original projectile already exists
                    // in this slot conceptually.
                    if (column == originalColumn &&
                        row == originalRow)
                    {
                        continue;
                    }

                    if (spawned >= totalProjectiles - 1)
                        return;

                    float columnOffset =
                        column -
                        ((columns - 1) / 2f);

                    float rowOffset =
                        row -
                        ((rows - 1) / 2f);

                    // Columns are perpendicular to travel.
                    // Rows are along the travel direction.
                    Vector2 offset =
                        sideways *
                        (columnOffset * GridSpacing);

                    offset +=
                        forward *
                        (rowOffset * GridSpacing);

                    Vector2 position =
                        args.Position + offset;

                    float ai0 =
                        args.Ai != null &&
                        args.Ai.Length > 0
                            ? args.Ai[0]
                            : 0f;

                    float ai1 =
                        args.Ai != null &&
                        args.Ai.Length > 1
                            ? args.Ai[1]
                            : 0f;

                    float ai2 =
                        args.Ai != null &&
                        args.Ai.Length > 2
                            ? args.Ai[2]
                            : 0f;

                    try
                    {
                        SpawnProjectile(
                            position,
                            velocity,
                            args.Type,
                            args.Damage,
                            args.Knockback,
                            args.Owner,
                            ai0,
                            ai1,
                            ai2);

                        spawned++;
                    }
                    catch (Exception ex)
                    {
                        TShock.Log.Error(
                            "[BPM] Failed to spawn projectile copy: " +
                            ex);

                        return;
                    }
                }
            }
        }

        private void SpawnProjectile(
            Vector2 position,
            Vector2 velocity,
            short type,
            short damage,
            float knockback,
            byte owner,
            float ai0,
            float ai1,
            float ai2)
        {
            if (newProjectileMethod == null)
                return;

            ParameterInfo[] parameters =
                newProjectileMethod
                    .GetParameters();

            object?[] values;

            // Terraria versions using:
            //
            // NewProjectile(
            //     IEntitySource,
            //     Vector2,
            //     Vector2,
            //     int,
            //     int,
            //     float,
            //     int,
            //     float,
            //     float,
            //     float)
            //
            // or the float-coordinate equivalent.
            if (parameters.Length == 11 ||
                parameters.Length == 12)
            {
                if (parameters[1].ParameterType ==
                    typeof(Vector2))
                {
                    if (parameters.Length == 11)
                    {
                        values = new object?[]
                        {
                            null,
                            position,
                            velocity,
                            (int)type,
                            (int)damage,
                            knockback,
                            (int)owner,
                            ai0,
                            ai1,
                            ai2
                        };
                    }
                    else
                    {
                        values = new object?[]
                        {
                            null,
                            position,
                            velocity,
                            (int)type,
                            (int)damage,
                            knockback,
                            (int)owner,
                            ai0,
                            ai1,
                            ai2,
                            0f
                        };
                    }
                }
                else
                {
                    if (parameters.Length == 11)
                    {
                        values = new object?[]
                        {
                            null,
                            position.X,
                            position.Y,
                            velocity.X,
                            velocity.Y,
                            (int)type,
                            (int)damage,
                            knockback,
                            (int)owner,
                            ai0,
                            ai1
                        };
                    }
                    else
                    {
                        values = new object?[]
                        {
                            null,
                            position.X,
                            position.Y,
                            velocity.X,
                            velocity.Y,
                            (int)type,
                            (int)damage,
                            knockback,
                            (int)owner,
                            ai0,
                            ai1,
                            ai2
                        };
                    }
                }
            }
            else
            {
                // Legacy overload:
                //
                // NewProjectile(
                //     float X,
                //     float Y,
                //     float SpeedX,
                //     float SpeedY,
                //     int Type,
                //     int Damage,
                //     float KnockBack,
                //     int Owner,
                //     float ai0,
                //     float ai1)
                values = new object?[]
                {
                    position.X,
                    position.Y,
                    velocity.X,
                    velocity.Y,
                    (int)type,
                    (int)damage,
                    knockback,
                    (int)owner,
                    ai0,
                    ai1
                };
            }

            newProjectileMethod.Invoke(
                null,
                values);
        }

        // =========================================================
        // MULTIPLIER DATA
        // =========================================================

        private string GetPlayerKey(
            TSPlayer player)
        {
            if (player.Account != null &&
                !string.IsNullOrWhiteSpace(
                    player.Account.Name))
            {
                return "account:" +
                    player.Account.Name
                        .ToLowerInvariant();
            }

            return "uuid:" +
                player.UUID;
        }

        private int GetMultiplier(
            TSPlayer player)
        {
            lock (sync)
            {
                if (multipliers.TryGetValue(
                    GetPlayerKey(player),
                    out int value))
                {
                    return value;
                }
            }

            return DefaultMultiplier;
        }

        private void SetMultiplier(
            TSPlayer player,
            int value)
        {
            value =
                Math.Clamp(
                    value,
                    DefaultMultiplier,
                    MaximumMultiplier);

            lock (sync)
            {
                multipliers[
                    GetPlayerKey(player)] =
                    value;
            }

            SaveData();
        }

        private void AddBossKill(
            TSPlayer player)
        {
            if (!Enabled ||
                player == null ||
                !player.Active)
            {
                return;
            }

            int oldValue =
                GetMultiplier(player);

            int newValue =
                Math.Min(
                    MaximumMultiplier,
                    oldValue + 1);

            SetMultiplier(
                player,
                newValue);

            player.SendSuccessMessage(
                "[BPM] Boss defeated! " +
                $"Your projectile multiplier is now {newValue}x.");
        }

        // =========================================================
        // COMMANDS
        // =========================================================

        private void BpmCommand(
            CommandArgs args)
        {
            if (args.Parameters.Count == 0)
            {
                ShowHelp(args.Player);
                return;
            }

            string command =
                args.Parameters[0]
                    .ToLowerInvariant();

            switch (command)
            {
                case "on":
                    if (!RequireAdmin(args))
                        return;

                    Enabled = true;
                    SaveData();

                    args.Player.SendSuccessMessage(
                        "[BPM] Enabled.");
                    break;

                case "off":
                    if (!RequireAdmin(args))
                        return;

                    Enabled = false;
                    SaveData();

                    args.Player.SendSuccessMessage(
                        "[BPM] Disabled.");
                    break;

                case "status":
                    args.Player.SendInfoMessage(
                        $"[BPM] Status: " +
                        $"{(Enabled ? "ON" : "OFF")}");

                    args.Player.SendInfoMessage(
                        $"[BPM] Multiplier: " +
                        $"{GetMultiplier(args.Player)}x");
                    break;

                case "count":
                    args.Player.SendInfoMessage(
                        $"[BPM] Projectile count: " +
                        $"{GetMultiplier(args.Player)}");
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
                        "[BPM] Reloaded.");
                    break;

                default:
                    ShowHelp(args.Player);
                    break;
            }
        }

        private bool RequireAdmin(
            CommandArgs args)
        {
            if (args.Player.HasPermission(
                Permission))
            {
                return true;
            }

            args.Player.SendErrorMessage(
                "You need the bpm.admin permission.");

            return false;
        }

        private void SetCommand(
            CommandArgs args)
        {
            if (!RequireAdmin(args))
                return;

            if (args.Parameters.Count < 3)
            {
                args.Player.SendErrorMessage(
                    "/bpm set <player> <number>");
                return;
            }

            if (!int.TryParse(
                    args.Parameters[2],
                    out int amount))
            {
                args.Player.SendErrorMessage(
                    "The number must be an integer.");
                return;
            }

            if (amount < DefaultMultiplier ||
                amount > MaximumMultiplier)
            {
                args.Player.SendErrorMessage(
                    $"Number must be between " +
                    $"{DefaultMultiplier} and " +
                    $"{MaximumMultiplier}.");
                return;
            }

            TSPlayer? target =
                FindPlayer(
                    args.Parameters[1]);

            if (target == null)
                return;

            SetMultiplier(
                target,
                amount);

            args.Player.SendSuccessMessage(
                $"[BPM] {target.Name} is now {amount}x.");
        }

        private void ResetCommand(
            CommandArgs args)
        {
            if (!RequireAdmin(args))
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage(
                    "/bpm reset <player>");
                return;
            }

            TSPlayer? target =
                FindPlayer(
                    args.Parameters[1]);

            if (target == null)
                return;

            SetMultiplier(
                target,
                DefaultMultiplier);

            args.Player.SendSuccessMessage(
                $"[BPM] {target.Name} reset to 1x.");
        }

        private TSPlayer? FindPlayer(
            string name)
        {
            TSPlayer[] players =
                TShock.Players
                    .Where(p =>
                        p != null &&
                        p.Active &&
                        (
                            p.Name.Equals(
                                name,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            p.Name.IndexOf(
                                name,
                                StringComparison.OrdinalIgnoreCase)
                            >= 0))
                    .ToArray();

            if (players.Length == 0)
            {
                TSPlayer.All.SendErrorMessage(
                    $"Player '{name}' was not found.");

                return null;
            }

            if (players.Length > 1)
            {
                TSPlayer.All.SendErrorMessage(
                    "Multiple players matched that name.");

                return null;
            }

            return players[0];
        }

        private void ShowHelp(
            TSPlayer player)
        {
            player.SendInfoMessage(
                "[BPM] /bpm count");

            player.SendInfoMessage(
                "[BPM] /bpm status");

            if (player.HasPermission(
                Permission))
            {
                player.SendInfoMessage(
                    "[BPM] /bpm on");

                player.SendInfoMessage(
                    "[BPM] /bpm off");

                player.SendInfoMessage(
                    "[BPM] /bpm set <player> <number>");

                player.SendInfoMessage(
                    "[BPM] /bpm reset <player>");

                player.SendInfoMessage(
                    "[BPM] /bpm reload");
            }
        }

        // =========================================================
        // SAVE / LOAD
        // =========================================================

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

                            if (bool.TryParse(
                                value,
                                out bool enabled))
                            {
                                Enabled = enabled;
                            }

                            continue;
                        }

                        string[] parts =
                            line.Split('\t');

                        if (parts.Length != 2)
                            continue;

                        if (!int.TryParse(
                                parts[1],
                                out int multiplier))
                        {
                            continue;
                        }

                        multiplier =
                            Math.Clamp(
                                multiplier,
                                DefaultMultiplier,
                                MaximumMultiplier);

                        multipliers[
                            parts[0]] =
                            multiplier;
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(
                        "[BPM] Failed to load data: " +
                        ex);
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
                        TShock.SavePath);

                    using StreamWriter writer =
                        new StreamWriter(
                            DataFile,
                            false);

                    writer.WriteLine(
                        "#enabled=" +
                        Enabled
                            .ToString()
                            .ToLowerInvariant());

                    foreach (
                        KeyValuePair<string, int> pair
                        in multipliers.OrderBy(
                            x => x.Key))
                    {
                        writer.WriteLine(
                            pair.Key +
                            "\t" +
                            pair.Value);
                    }
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(
                        "[BPM] Failed to save data: " +
                        ex);
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
                    OnPlayerLeave);

                GetDataHandlers.NewProjectile.UnRegister(
                    OnNewProjectile);
            }

            base.Dispose(disposing);
        }
    }
}
