using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace LeaderboardBlock
{
    [BepInPlugin(Id, "Leaderboard Block", "1.0.1")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "local.leaderboardblock";
        internal static Plugin Instance;
        private readonly Dictionary<string, bool> decisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<GorillaPlayerScoreboardLine, BlockRow> rows = new Dictionary<GorillaPlayerScoreboardLine, BlockRow>();
        private readonly List<GorillaPlayerScoreboardLine> staleRows = new List<GorillaPlayerScoreboardLine>();
        private readonly HashSet<RigContainer> trackedRigs = new HashSet<RigContainer>();
        private readonly List<RigContainer> staleRigs = new List<RigContainer>();
        private BlockList blocks;
        private VisibilityMask visuals;
        private Harmony harmony;
        private float nextReconcile;
        private bool ready;
        private bool visualsDirty;
        internal static bool ChangingMute;
        private static readonly FieldInfo ManualMute = AccessTools.Field(typeof(GorillaPlayerScoreboardLine), "isMuteManual");
        private static readonly FieldInfo MuteValue = AccessTools.Field(typeof(GorillaPlayerScoreboardLine), "mute");

        private void Awake()
        {
            Instance = this;
            try
            {
                blocks = new BlockList(Path.Combine(Paths.ConfigPath, "LeaderboardBlock", "blocked-players.txt"));
                blocks.Load();
                visuals = new VisibilityMask();
                harmony = new Harmony(Id);
                harmony.PatchAll(typeof(Plugin).Assembly);
                ready = true;
                VRRigCache.OnRigActivated += RigJoined;
                VRRigCache.OnRigDeactivated += RigLeft;
                VRRigCache.OnPostInitialize += Reconcile;
                Application.onBeforeRender += BeforeRender;
                Reconcile();
                Logger.LogInfo("Leaderboard Block loaded. " + blocks.Count + " saved blocks.");
            }
            catch (Exception error)
            {
                Logger.LogError("Leaderboard Block could not start: " + error);
                Shutdown();
            }
        }

        internal static bool IsRemote(NetPlayer player)
        {
            return player != null && !player.IsNull && !player.IsLocal && player.InRoom &&
                   !string.IsNullOrWhiteSpace(player.UserId);
        }

        internal bool IsBlocked(NetPlayer player)
        {
            if (!ready || !IsRemote(player)) return false;
            bool value;
            if (!decisions.TryGetValue(player.UserId, out value))
            {
                value = blocks.Contains(player.UserId);
                decisions[player.UserId] = value;
            }
            return value;
        }

        internal bool IsBlocked(VRRig rig)
        {
            return rig && !rig.isOfflineVRRig && !rig.isMyPlayer && IsBlocked(rig.Creator);
        }

        internal bool Toggle(GorillaPlayerScoreboardLine line)
        {
            if (!ready || !line || !IsRemote(line.linePlayer))
            {
                Trace("BLOCK press ignored: player is local, has left, or has no stable account ID yet.");
                return false;
            }
            string name = (line.linePlayer.NickName ?? "(unnamed)").Replace('\r', ' ').Replace('\n', ' ');
            Trace("Target player: " + name);
            string id = line.linePlayer.UserId;
            bool blocked = !IsBlocked(line.linePlayer);
            try { blocks.Set(id, blocked); }
            catch (Exception error)
            {
                Logger.LogError("Could not save block change; the previous state is unchanged. " + error.Message);
                return false;
            }
            decisions[id] = blocked;
            PlayerPrefs.SetInt(id, blocked ? 1 : 0);
            PlayerPrefs.Save();
            Trace(blocked ? "Muting player." : "Removing block mute.");
            var rigs = VRRigCache.ActiveRigContainers;
            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig && rig.Rig && IsRemote(rig.Creator) && rig.Creator.UserId == id)
                {
                    if (blocked) trackedRigs.Add(rig); else trackedRigs.Remove(rig);
                    SetNativeMute(rig, blocked);
                }
            }
            foreach (var row in rows)
            {
                if (!row.Key || !IsRemote(row.Key.linePlayer) || row.Key.linePlayer.UserId != id) continue;
                SetRowMute(row.Key, blocked);
                if (row.Value) row.Value.Refresh(blocked);
            }
            Trace(blocked ? "Hiding player." : "Restoring player visuals.");
            RefreshVisuals();
            Trace(blocked ? "Block enabled successfully." : "Block disabled successfully.");
            return true;
        }

        private static void SetNativeMute(RigContainer rig, bool muted)
        {
            if (!rig || !rig.Rig || rig.Rig.isOfflineVRRig || rig.Rig.isMyPlayer || !IsRemote(rig.Creator)) return;
            rig.hasManualMute = true;
            ChangingMute = true;
            try
            {
                rig.SetMuted(RigContainer.MuteReason.Manual, muted);
                // Refresh also removes a previous automatic mute when explicitly unblocking.
                rig.RefreshVoiceChat();
            }
            finally { ChangingMute = false; }
            if (rig.ReplacementVoiceSource && muted) rig.ReplacementVoiceSource.mute = true;
        }

        private static void SetRowMute(GorillaPlayerScoreboardLine line, bool muted)
        {
            ManualMute.SetValue(line, true);
            MuteValue.SetValue(line, muted ? 1 : 0);
            if (!line.muteButton) return;
            line.muteButton.isOn = muted;
            line.muteButton.isAutoOn = false;
            line.muteButton.UpdateColor();
        }

        internal void SyncRow(GorillaPlayerScoreboardLine line)
        {
            if (!ready || !line || !line.muteButton || !line.playerSwatch) return;
            BlockRow row;
            if (!rows.TryGetValue(line, out row) || !row)
            {
                row = line.GetComponent<BlockRow>();
                if (!row) row = line.gameObject.AddComponent<BlockRow>();
                if (!row.Initialize(line)) return;
                rows[line] = row;
            }
            bool blocked = IsBlocked(line.linePlayer);
            if (blocked) SetRowMute(line, true);
            row.Refresh(blocked);
        }

        internal void ForgetRow(GorillaPlayerScoreboardLine line) { rows.Remove(line); }

        private void RigJoined(RigContainer rig)
        {
            if (!ready || !rig || !IsBlocked(rig.Rig)) return;
            trackedRigs.Add(rig);
            if (PlayerPrefs.GetInt(rig.Creator.UserId, 0) != 1 || !PlayerPrefs.HasKey(rig.Creator.UserId))
            {
                PlayerPrefs.SetInt(rig.Creator.UserId, 1);
                PlayerPrefs.Save();
            }
            SetNativeMute(rig, true);
            RefreshVisuals();
        }

        private void RigLeft(RigContainer rig)
        {
            if (!ready) return;
            trackedRigs.Remove(rig);
            decisions.Clear();
            RefreshVisuals();
        }

        internal void CosmeticsChanged() { if (ready) visualsDirty = true; }

        private void Reconcile()
        {
            if (!ready) return;
            staleRigs.Clear();
            foreach (var rig in trackedRigs)
                if (!rig || !rig.gameObject.activeInHierarchy || !IsBlocked(rig.Rig)) staleRigs.Add(rig);
            foreach (var rig in staleRigs) trackedRigs.Remove(rig);
            var rigs = VRRigCache.ActiveRigContainers;
            for (int i = 0; i < rigs.Count; i++)
            {
                if (!rigs[i] || !IsBlocked(rigs[i].Rig)) continue;
                if (!trackedRigs.Contains(rigs[i])) RigJoined(rigs[i]);
                else if (!rigs[i].IsMutedFor(RigContainer.MuteReason.Manual)) SetNativeMute(rigs[i], true);
            }
            staleRows.Clear();
            foreach (var row in rows) if (!row.Key || !row.Value) staleRows.Add(row.Key);
            foreach (var row in staleRows) rows.Remove(row);
            var lines = GorillaScoreboardTotalUpdater.allScoreboardLines;
            if (lines != null)
                for (int i = 0; i < lines.Count; i++) SyncRow(lines[i]);
            RefreshVisuals();
        }

        private void LateUpdate()
        {
            if (!ready) return;
            Guard(Tick);
        }

        private void Tick()
        {
            if (Time.unscaledTime >= nextReconcile)
            {
                nextReconcile = Time.unscaledTime + 0.5f;
                Reconcile();
            }
            if (visualsDirty) RefreshVisuals();
            visuals.Apply();
        }

        private void BeforeRender()
        {
            if (!ready) return;
            Guard(() =>
            {
                if (visualsDirty) RefreshVisuals();
                visuals.Apply();
            });
        }

        private void RefreshVisuals()
        {
            visualsDirty = false;
            visuals.Refresh(trackedRigs, this);
        }

        private void OnDestroy() { Shutdown(); }

        private void Shutdown()
        {
            ready = false;
            VRRigCache.OnRigActivated -= RigJoined;
            VRRigCache.OnRigDeactivated -= RigLeft;
            VRRigCache.OnPostInitialize -= Reconcile;
            Application.onBeforeRender -= BeforeRender;
            if (visuals != null) visuals.Restore();
            if (harmony != null) harmony.UnpatchSelf();
            foreach (var row in rows) if (row.Value) row.Value.Dispose();
            rows.Clear();
            trackedRigs.Clear();
            decisions.Clear();
            if (Instance == this) Instance = null;
        }

        internal static void Guard(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                if (Instance) Instance.Logger.LogError("Leaderboard Block: " + error);
            }
        }

        internal static void Trace(string message)
        {
            if (Instance) Instance.Logger.LogInfo(message);
        }
    }
}
