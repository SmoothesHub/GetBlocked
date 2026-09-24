using System.Collections.Generic;
using UnityEngine;

namespace LeaderboardBlock
{
    internal sealed class VisibilityMask
    {
        private sealed class Saved<T> where T : Component
        {
            internal T Item;
            internal VRRig Rig;
            internal Transform Root;
            internal TransferrableObject Holdable;
            internal bool Before;
            internal int Seen;
            internal string OwnerId;
        }

        private readonly Dictionary<Renderer, Saved<Renderer>> renderers = new Dictionary<Renderer, Saved<Renderer>>();
        private readonly Dictionary<Canvas, Saved<Canvas>> canvases = new Dictionary<Canvas, Saved<Canvas>>();
        private readonly Dictionary<Light, Saved<Light>> lights = new Dictionary<Light, Saved<Light>>();
        private readonly List<Renderer> rendererBuffer = new List<Renderer>(256);
        private readonly List<Canvas> canvasBuffer = new List<Canvas>();
        private readonly List<Light> lightBuffer = new List<Light>();
        private readonly List<Renderer> removedRenderers = new List<Renderer>();
        private readonly List<Canvas> removedCanvases = new List<Canvas>();
        private readonly List<Light> removedLights = new List<Light>();
        private readonly HashSet<GameObject> roots = new HashSet<GameObject>();
        private int generation;

        internal void Refresh(IEnumerable<RigContainer> rigs, Plugin plugin)
        {
            generation++;
            roots.Clear();
            foreach (var container in rigs)
            {
                if (!container || !container.gameObject.activeInHierarchy || !plugin.IsBlocked(container.Rig)) continue;
                VRRig rig = container.Rig;
                Gather(rig.gameObject, rig);
                if (rig.nameTagAnchor) Gather(rig.nameTagAnchor, rig);
                for (int i = 0; i < rig.activeCosmetics.Count; i++) Gather(rig.activeCosmetics[i], rig);
                // Holdables can leave the rig hierarchy when equipped, but remain in its dock registry.
                if (rig.myBodyDockPositions)
                {
                    var items = rig.myBodyDockPositions.allObjects;
                    if (items != null)
                        for (int i = 0; i < items.Length; i++)
                            if (items[i] && BelongsTo(items[i], rig)) Gather(items[i].gameObject, rig);
                }
            }
            Prune();
            Apply();
        }

        private static bool BelongsTo(TransferrableObject item, VRRig rig)
        {
            if (!item) return true;
            if (item.targetRig) return item.targetRig == rig;
            if (item.isSceneObject) return false;
            if (item.ownerRig) return item.ownerRig == rig;
            if (item.myOnlineRig) return item.myOnlineRig == rig;
            if (item.myRig) return item.myRig == rig;
            return item.transform.IsChildOf(rig.transform);
        }

        private void Gather(GameObject root, VRRig rig)
        {
            if (!root || !roots.Add(root)) return;
            if (root != rig.gameObject && root.transform.IsChildOf(rig.transform)) return;
            root.GetComponentsInChildren(true, rendererBuffer);
            for (int i = 0; i < rendererBuffer.Count; i++)
            {
                Renderer item = rendererBuffer[i];
                VRRig parentRig = item.GetComponentInParent<VRRig>(true);
                var holdable = item.GetComponentInParent<TransferrableObject>(true);
                if ((parentRig && parentRig != rig) || !BelongsTo(holdable, rig)) continue;
                Saved<Renderer> saved;
                if (!renderers.TryGetValue(item, out saved))
                {
                    saved = new Saved<Renderer> { Item = item, Before = item.forceRenderingOff };
                    renderers.Add(item, saved);
                }
                Stamp(saved, rig, root.transform, holdable);
            }
            root.GetComponentsInChildren(true, canvasBuffer);
            for (int i = 0; i < canvasBuffer.Count; i++)
            {
                Canvas item = canvasBuffer[i];
                VRRig parentRig = item.GetComponentInParent<VRRig>(true);
                var holdable = item.GetComponentInParent<TransferrableObject>(true);
                if ((parentRig && parentRig != rig) || !BelongsTo(holdable, rig)) continue;
                Saved<Canvas> saved;
                if (!canvases.TryGetValue(item, out saved))
                {
                    saved = new Saved<Canvas> { Item = item, Before = item.enabled };
                    canvases.Add(item, saved);
                }
                Stamp(saved, rig, root.transform, holdable);
            }
            root.GetComponentsInChildren(true, lightBuffer);
            for (int i = 0; i < lightBuffer.Count; i++)
            {
                Light item = lightBuffer[i];
                VRRig parentRig = item.GetComponentInParent<VRRig>(true);
                var holdable = item.GetComponentInParent<TransferrableObject>(true);
                if ((parentRig && parentRig != rig) || !BelongsTo(holdable, rig)) continue;
                Saved<Light> saved;
                if (!lights.TryGetValue(item, out saved))
                {
                    saved = new Saved<Light> { Item = item, Before = item.enabled };
                    lights.Add(item, saved);
                }
                Stamp(saved, rig, root.transform, holdable);
            }
        }

        private void Stamp<T>(Saved<T> saved, VRRig rig, Transform root, TransferrableObject holdable) where T : Component
        {
            saved.Rig = rig;
            saved.Root = root;
            saved.Holdable = holdable;
            saved.Seen = generation;
            saved.OwnerId = rig.Creator.UserId;
        }

        private static bool StillOwned<T>(Saved<T> saved) where T : Component
        {
            if (!saved.Item || !saved.Rig || !saved.Rig.gameObject.activeInHierarchy ||
                saved.Rig.isOfflineVRRig || saved.Rig.isMyPlayer || !Plugin.IsRemote(saved.Rig.Creator) ||
                saved.Rig.Creator.UserId != saved.OwnerId) return false;
            if (saved.Holdable) return BelongsTo(saved.Holdable, saved.Rig);
            if (!saved.Root || !saved.Item.transform.IsChildOf(saved.Root)) return false;
            if (saved.Root != saved.Rig.transform && !saved.Root.IsChildOf(saved.Rig.transform))
            {
                var newOwner = saved.Root.GetComponentInParent<VRRig>(true);
                if (newOwner && newOwner != saved.Rig) return false;
            }
            return true;
        }

        internal void Apply()
        {
            // Only cached visuals are touched per frame. Gameplay components keep running.
            foreach (var pair in renderers)
            {
                var saved = pair.Value;
                if (saved.Item) saved.Item.forceRenderingOff = StillOwned(saved) || saved.Before;
            }
            foreach (var pair in canvases)
            {
                var saved = pair.Value;
                if (saved.Item) saved.Item.enabled = !StillOwned(saved) && saved.Before;
            }
            foreach (var pair in lights)
            {
                var saved = pair.Value;
                if (saved.Item) saved.Item.enabled = !StillOwned(saved) && saved.Before;
            }
        }

        private void Prune()
        {
            removedRenderers.Clear();
            foreach (var pair in renderers)
                if (!pair.Key || pair.Value.Seen != generation)
                {
                    if (pair.Key) pair.Key.forceRenderingOff = pair.Value.Before;
                    removedRenderers.Add(pair.Key);
                }
            foreach (var item in removedRenderers) renderers.Remove(item);
            removedCanvases.Clear();
            foreach (var pair in canvases)
                if (!pair.Key || pair.Value.Seen != generation)
                {
                    if (pair.Key) pair.Key.enabled = pair.Value.Before;
                    removedCanvases.Add(pair.Key);
                }
            foreach (var item in removedCanvases) canvases.Remove(item);
            removedLights.Clear();
            foreach (var pair in lights)
                if (!pair.Key || pair.Value.Seen != generation)
                {
                    if (pair.Key) pair.Key.enabled = pair.Value.Before;
                    removedLights.Add(pair.Key);
                }
            foreach (var item in removedLights) lights.Remove(item);
            rendererBuffer.Clear();
            canvasBuffer.Clear();
            lightBuffer.Clear();
        }

        internal void Restore()
        {
            generation++;
            Prune();
            roots.Clear();
        }
    }
}
