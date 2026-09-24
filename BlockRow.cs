using UnityEngine;
using TMPro;

namespace LeaderboardBlock
{
    internal sealed class BlockRow : MonoBehaviour
    {
        internal GorillaPlayerScoreboardLine Line { get; private set; }
        internal GorillaPlayerLineButton Button { get; private set; }
        private TextMeshPro label;
        private Vector3 speakerPosition;
        private bool movedSpeaker;
        private bool disposed;
        private bool lastState;
        private bool painted;

        internal bool Initialize(GorillaPlayerScoreboardLine line)
        {
            if (Button) return true;
            Line = line;
            var source = line.muteButton;
            if (!source || !source.myText || !line.playerSwatch) return false;
            // Instantiate the actual native button, including its mesh, collider and materials.
            Button = Instantiate(source, source.transform.parent, false);
            Button.name = "LeaderboardBlockButton";
            Button.parentLine = line;
            Button.offText = Button.onText = Button.autoOnText = "BLOCK";
            Button.isAutoOn = false;
            Button.isOn = false;
            Button.testPress = false;
            Button.touchTime = Time.time;
            Button.debounceTime = 0.3f;
            Button.enabled = false;
            var marker = Button.gameObject.AddComponent<BlockButton>();
            marker.Row = this;
            foreach (var collider in Button.GetComponents<Collider>())
            {
                collider.enabled = true;
                collider.isTrigger = true;
            }

            Transform parent = source.transform.parent;
            Vector3 swatch = parent.InverseTransformPoint(line.playerSwatch.rectTransform.TransformPoint(line.playerSwatch.rectTransform.rect.center));
            Vector3 position = source.transform.localPosition;
            var box = Button.GetComponent<BoxCollider>();
            float buttonWidth = box ? Mathf.Abs(box.size.x * Button.transform.localScale.x) : 12f;
            position.x -= buttonWidth * 1.15f;
            Button.transform.localPosition = position;

            // The voice indicator occupies the insertion gap in the current prefab.
            // Keep COLOR, MUTE and REPORT fixed; tuck the indicator between COLOR and BLOCK.
            if (line.speakerIcon)
            {
                speakerPosition = line.speakerIcon.transform.localPosition;
                Vector3 speaker = parent.InverseTransformPoint(line.speakerIcon.transform.position);
                float halfWidth = buttonWidth * 0.5f;
                if (Mathf.Abs(speaker.x - position.x) < halfWidth + 3f)
                {
                    speaker.x = (swatch.x + position.x) * 0.5f;
                    line.speakerIcon.transform.position = parent.TransformPoint(speaker);
                    movedSpeaker = true;
                }
            }

            Button.myText.text = "BLOCK";
            if (line.parentScoreboard && line.parentScoreboard.buttonText)
            {
                var template = line.parentScoreboard.buttonText;
                label = Instantiate(template, Button.transform, true);
                label.name = "BlockLabel";
                label.text = "BLOCK";
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.margin = Vector4.zero;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                label.transform.position = Button.myText.transform.position;
                label.rectTransform.sizeDelta = new Vector2(0.98f / Mathf.Abs(label.transform.localScale.x),
                    0.9f / Mathf.Abs(label.transform.localScale.y));
                label.enabled = true;
                label.gameObject.SetActive(true);
                Button.myText.gameObject.SetActive(false);
            }
            else
            {
                Button.myText.enabled = true;
                Button.myText.gameObject.SetActive(true);
            }
            Button.UpdateColor();
            return true;
        }

        internal void Refresh(bool blocked)
        {
            if (!Button || !Line) return;
            bool visible = Plugin.IsRemote(Line.linePlayer) && Line.muteButton && Line.muteButton.gameObject.activeSelf;
            if (Button.gameObject.activeSelf != visible) Button.gameObject.SetActive(visible);
            if (!painted || lastState != blocked)
            {
                painted = true;
                lastState = blocked;
                Button.isOn = blocked;
                Button.isAutoOn = false;
                Button.UpdateColor();
            }
        }

        internal void Click(bool leftHand)
        {
            if (!Plugin.Instance || !Line || !Button || !Button.gameObject.activeInHierarchy)
            {
                Plugin.Trace("BLOCK press ignored: its player row is unavailable.");
                return;
            }
            if (!Plugin.Instance.Toggle(Line)) return;
            var tagger = GorillaTagger.Instance;
            if (tagger)
            {
                tagger.StartVibration(leftHand, tagger.tapHapticStrength * 0.5f, tagger.tapHapticDuration);
                if (tagger.offlineVRRig) tagger.offlineVRRig.PlayHandTapLocal(67, leftHand, 0.05f);
            }
        }

        internal void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (movedSpeaker && Line && Line.speakerIcon) Line.speakerIcon.transform.localPosition = speakerPosition;
            if (Button)
            {
                Button.gameObject.SetActive(false);
                Destroy(Button.gameObject);
            }
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (Plugin.Instance && Line) Plugin.Instance.ForgetRow(Line);
            if (Button) Destroy(Button.gameObject);
        }
    }

}
