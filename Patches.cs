using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace LeaderboardBlock
{
    [HarmonyPatch]
    internal static class RowChanged
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GorillaPlayerScoreboardLine), "InitializeLine");
            yield return AccessTools.Method(typeof(GorillaPlayerScoreboardLine), "UpdateLine");
            yield return AccessTools.Method(typeof(GorillaPlayerScoreboardLine), "ResetData");
        }
        private static void Postfix(GorillaPlayerScoreboardLine __instance)
        {
            if (Plugin.Instance) Plugin.Guard(() => Plugin.Instance.SyncRow(__instance));
        }
    }

    [HarmonyPatch(typeof(GorillaPlayerLineButton), "Click")]
    internal static class ButtonClicked
    {
        private static bool Prefix(GorillaPlayerLineButton __instance, bool leftHand)
        {
            if (!Plugin.Instance) return true;
            var block = __instance.GetComponent<BlockButton>();
            if (block)
            {
                block.Click(leftHand);
                return false;
            }
            // Intercept before the native handler flips isOn or sends its click RPC.
            if (__instance.buttonType == GorillaPlayerLineButton.ButtonType.Mute &&
                __instance.parentLine && Plugin.Instance.IsBlocked(__instance.parentLine.linePlayer))
            {
                Plugin.Instance.SyncRow(__instance.parentLine);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GorillaPlayerScoreboardLine), "PressButton")]
    internal static class MutePressed
    {
        private static bool Prefix(GorillaPlayerScoreboardLine __instance, GorillaPlayerLineButton.ButtonType buttonType)
        {
            if (!Plugin.Instance || buttonType != GorillaPlayerLineButton.ButtonType.Mute ||
                !Plugin.Instance.IsBlocked(__instance.linePlayer)) return true;
            Plugin.Instance.SyncRow(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(RigContainer), "SetMuted")]
    internal static class KeepMuted
    {
        private static void Prefix(RigContainer __instance, ref RigContainer.MuteReason reasons, bool muted)
        {
            if (Plugin.Instance && Plugin.Instance.IsBlocked(__instance.Rig))
            {
                __instance.hasManualMute = true;
                if (!muted) reasons &= ~RigContainer.MuteReason.Manual;
            }
        }
    }

    [HarmonyPatch]
    internal static class CosmeticsChanged
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(VRRig), "LocalUpdateCosmeticsWithTryon");
            yield return AccessTools.Method(typeof(VRRig), "SetCosmeticsActive");
            yield return AccessTools.Method(typeof(GorillaNetworking.CosmeticItemRegistry), "InitializeCosmetic");
            yield return AccessTools.Method(typeof(TransferrableObject), "SetTargetRig");
            yield return AccessTools.Method(typeof(TransferrableObject), "OnSpawn");
            yield return AccessTools.Method(typeof(TransferrableObject), "OnDespawn");
        }
        private static void Postfix()
        {
            if (Plugin.Instance) Plugin.Instance.CosmeticsChanged();
        }
    }

    [HarmonyPatch(typeof(RigContainer), "RefreshVoiceChat")]
    internal static class VoiceLoading
    {
        private static bool Prefix(RigContainer __instance)
        {
            if (!Plugin.Instance || (!Plugin.ChangingMute && !Plugin.Instance.IsBlocked(__instance.Rig))) return true;
            if (!__instance.Voice) return true;
            // Voice initialization assigns these references in separate steps.
            return __instance.Voice.SpeakerInUse && __instance.ReplacementVoiceSource &&
                   GorillaNetworking.GorillaComputer.instance;
        }
    }
}
