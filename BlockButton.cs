using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LeaderboardBlock
{
    internal sealed class BlockButton : MonoBehaviour, IClickable, IPointerClickHandler
    {
        internal BlockRow Row;
        private readonly HashSet<Collider> contacts = new HashSet<Collider>();
        private float nextPressTime;

        private void OnEnable()
        {
            contacts.Clear();
            nextPressTime = float.NegativeInfinity;
        }

        private void OnDisable() { contacts.Clear(); }

        private void OnTriggerEnter(Collider other)
        {
            if (!other || !isActiveAndEnabled) return;
            var hand = other.GetComponentInParent<GorillaTriggerColliderHandIndicator>();
            if (!hand) return;
            bool alreadyTouching = contacts.Count != 0;
            if (!contacts.Add(other) || alreadyTouching) return;
            Press(hand.isLeftHand, "hand collider");
        }

        private void OnTriggerStay(Collider other)
        {
            // Also handles a button becoming active while a hand is already overlapping it.
            OnTriggerEnter(other);
        }

        private void OnTriggerExit(Collider other) { contacts.Remove(other); }

        private void OnMouseDown() { Press(false, "mouse collider"); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Left)
                Press(false, "pointer");
        }

        public void Click(bool leftHand = false) { Press(leftHand, "clickable"); }

        private void Press(bool leftHand, string input)
        {
            if (!isActiveAndEnabled || Time.unscaledTime < nextPressTime) return;
            nextPressTime = Time.unscaledTime + 0.3f;
            Plugin.Trace("BLOCK button pressed (" + input + ").");
            if (!Row)
            {
                Plugin.Trace("BLOCK press ignored: no player row is attached.");
                return;
            }
            Plugin.Guard(() => Row.Click(leftHand));
        }
    }
}
