using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LOR_DiceSystem;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DeckOrdering
{
    [HarmonyPatch(typeof(UIEquipDeckCardList), nameof(UIEquipDeckCardList.SetData))]
    internal static class AttachDeckSwap
    {
        private static void Postfix(UIEquipDeckCardList __instance)
        {
            if (__instance.GetComponent<DeckSwapController>() == null)
                __instance.gameObject.AddComponent<DeckSwapController>();
        }
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerClick))]
    internal static class SuppressRemoveAfterDrag
    {
        private static bool Prefix() => !DeckSwapController.ShouldSuppressClick;
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerEnter))]
    internal static class SuppressDeckSlotEnterWhileDragging
    {
        private static bool Prefix() => !DeckSwapController.IsDragging;
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerExit))]
    internal static class SuppressDeckSlotExitWhileDragging
    {
        private static bool Prefix() => !DeckSwapController.IsDragging;
    }

    [HarmonyPatch]
    internal static class SuppressCardPreviewWhileDragging
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(UIEquipDeckCardList), nameof(UIEquipDeckCardList.ShowDetailSlotByInventory));
            yield return AccessTools.Method(typeof(UIInvenCardListScroll), nameof(UIInvenCardListScroll.ShowDetailSlotByInventory));
            yield return AccessTools.Method(typeof(UIEquipCardList), nameof(UIEquipCardList.ShowDetailSlotByInventory));
        }

        private static bool Prefix() => !DeckSwapController.IsDragging;
    }

    internal class DeckSwapController : MonoBehaviour
    {
        private const float LongPressSeconds = 0.35f;
        private const float DragThresholdPixels = 12f;
        private const float GhostAlpha = 0.45f;

        private static readonly AccessTools.FieldRef<UIOriginCardList, List<UIOriginCardSlot>> SlotList =
            AccessTools.FieldRefAccess<UIOriginCardList, List<UIOriginCardSlot>>("slotList");
        private static readonly AccessTools.FieldRef<UIOriginCardList, List<DiceCardItemModel>> ViewList =
            AccessTools.FieldRefAccess<UIOriginCardList, List<DiceCardItemModel>>("currentviewCardList");
        private static readonly AccessTools.FieldRef<BookModel, DeckModel> CurrentDeck =
            AccessTools.FieldRefAccess<BookModel, DeckModel>("_deck");
        private static readonly MethodInfo SetHighlightedSlot = AccessTools.Method(typeof(UIOriginCardSlot), "SetHighlightedSlot");

        private static bool anyDragging;
        private static int suppressUntilFrame = -1;

        internal static bool ShouldSuppressClick => anyDragging || Time.frameCount <= suppressUntilFrame;
        internal static bool IsDragging => anyDragging;

        private UIEquipDeckCardList list;
        private int pressIndex = -1;
        private float pressTime;
        private Vector2 pressPosition;
        private bool dragging;
        private int hoverIndex = -1;

        private GameObject ghost;
        private Vector3 ghostOffset;
        private Canvas rootCanvas;
        private Camera canvasCamera;
        private SwapArrow arrow;

        private void Awake()
        {
            list = GetComponent<UIEquipDeckCardList>();
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                Debug.Log(Initializer.LogPrefix + "Deck swap failed: " + e);
                Cancel();
            }
        }

        private void Tick()
        {
            if (list == null)
                return;
            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                Cancel();
                int index = SlotIndexAt(mouse);
                if (index >= 0 && CanEdit())
                {
                    pressIndex = index;
                    pressTime = Time.unscaledTime;
                    pressPosition = mouse;
                }
                return;
            }

            if (pressIndex < 0)
                return;

            if (Input.GetMouseButton(0))
            {
                if (dragging)
                    UpdateDrag(mouse);
                else if (Time.unscaledTime - pressTime >= LongPressSeconds || (mouse - pressPosition).magnitude >= DragThresholdPixels)
                    BeginDrag(mouse);
                return;
            }

            int source = pressIndex;
            bool wasDragging = dragging;
            int target = dragging ? SlotIndexAt(mouse) : -1;
            Cancel();
            if (target >= 0 && target != source && CanEdit())
                Swap(source, target);

            if (wasDragging)
                Highlight(SlotIndexAt(mouse), true);
        }

        private bool CanEdit()
        {
            UnitDataModel unit = list.currentunit;
            if (unit?.bookItem == null || list.IsDisabled)
                return false;
            UIPhase phase = UI.UIController.Instance.CurrentUIPhase;
            if (phase != UIPhase.Librarian_CardList && phase != UIPhase.BattleSetting)
                return false;
            if (list.rootPanel != null && list.rootPanel.IsLockBattleBluePrimary())
                return false;
            return !unit.bookItem.IsLockByBluePrimary();
        }

        private int SlotIndexAt(Vector2 screen)
        {
            List<UIOriginCardSlot> slots = SlotList(list);
            int count = Math.Min(ViewList(list).Count, slots.Count);
            for (int i = 0; i < count; i++)
            {
                UIOriginCardSlot slot = slots[i];
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.CardModel == null)
                    continue;
                RectTransform rt = (slot.selectable != null ? slot.selectable.transform : slot.transform) as RectTransform;
                if (rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, CameraFor(slot)))
                    return i;
            }
            return -1;
        }

        private static Camera CameraFor(Component c)
        {
            Canvas canvas = c.GetComponentInParent<Canvas>();
            if (canvas == null)
                return null;
            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private Vector3 ScreenToWorld(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)rootCanvas.transform, screen, canvasCamera, out Vector3 world);
            return world;
        }

        private void BeginDrag(Vector2 mouse)
        {
            UIOriginCardSlot slot = SlotList(list)[pressIndex];
            Canvas canvas = slot.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                pressIndex = -1;
                return;
            }

            dragging = true;
            anyDragging = true;
            rootCanvas = canvas.rootCanvas;
            canvasCamera = CameraFor(slot);
            HideAllCardPreviews();

            Highlight(pressIndex, true);

            arrow = SwapArrow.Create(rootCanvas.transform);

            ghost = Instantiate(slot.gameObject, rootCanvas.transform, true);
            ghost.name = "DeckOrdering_DragGhost";
            ghost.transform.SetAsLastSibling();

            FindTopSorting(out int topLayer, out int topOrder);
            DrawOnTop(arrow.gameObject, topLayer, topOrder);
            DrawOnTop(ghost, topLayer, topOrder + 1);
            foreach (Selectable s in ghost.GetComponentsInChildren<Selectable>(true))
                Destroy(s);
            foreach (EventTrigger t in ghost.GetComponentsInChildren<EventTrigger>(true))
                Destroy(t);
            foreach (UIOriginCardSlot s in ghost.GetComponentsInChildren<UIOriginCardSlot>(true))
                s.enabled = false;
            CanvasGroup ghostGroup = ghost.GetComponent<CanvasGroup>();
            if (ghostGroup == null)
                ghostGroup = ghost.AddComponent<CanvasGroup>();
            ghostGroup.alpha = GhostAlpha;
            ghostGroup.blocksRaycasts = false;
            ghostGroup.interactable = false;
            ghostOffset = slot.transform.position - ScreenToWorld(mouse);

            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Over);
            UpdateDrag(mouse);
        }

        private void FindTopSorting(out int layerId, out int order)
        {
            layerId = rootCanvas.sortingLayerID;
            order = rootCanvas.sortingOrder;
            int layerValue = SortingLayer.GetLayerValueFromID(layerId);
            foreach (Canvas c in FindObjectsOfType<Canvas>())
            {
                if (!c.isActiveAndEnabled || !(c.isRootCanvas || c.overrideSorting))
                    continue;
                Canvas root = c.rootCanvas;
                if (root.renderMode != rootCanvas.renderMode || root.worldCamera != rootCanvas.worldCamera)
                    continue;
                int value = SortingLayer.GetLayerValueFromID(c.sortingLayerID);
                if (value > layerValue || (value == layerValue && c.sortingOrder > order))
                {
                    layerValue = value;
                    layerId = c.sortingLayerID;
                    order = c.sortingOrder;
                }
            }
            order = Math.Min(order + 10, short.MaxValue - 1);
        }

        private static void DrawOnTop(GameObject go, int layerId, int order)
        {
            Canvas canvas = go.GetComponent<Canvas>();
            if (canvas == null)
                canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = layerId;
            canvas.sortingOrder = order;
        }

        private void HideAllCardPreviews()
        {
            list.HideDetailSlotByInventory();
            foreach (UIInvenCardListScroll inventory in FindObjectsOfType<UIInvenCardListScroll>())
                inventory.HideDetailSlotByInventory();
            foreach (UIEquipCardList equipped in FindObjectsOfType<UIEquipCardList>())
                equipped.HideDetailSlotByInventory();
        }

        private void UpdateDrag(Vector2 mouse)
        {
            if (ghost != null)
                ghost.transform.position = ScreenToWorld(mouse) + ghostOffset;

            int hover = SlotIndexAt(mouse);
            if (hover == pressIndex)
                hover = -1;
            if (hover == hoverIndex)
                return;

            Highlight(hoverIndex, false);
            hoverIndex = hover;
            if (hoverIndex < 0)
            {
                arrow?.Hide();
                return;
            }

            Highlight(hoverIndex, true);
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Over);
            arrow?.Show(SlotCenter(pressIndex), SlotCenter(hoverIndex));
        }

        private Vector3 SlotCenter(int index)
        {
            UIOriginCardSlot slot = SlotList(list)[index];
            RectTransform rt = (slot.selectable != null ? slot.selectable.transform : slot.transform) as RectTransform;
            return rt != null ? rt.TransformPoint(rt.rect.center) : slot.transform.position;
        }

        private void Highlight(int index, bool on)
        {
            List<UIOriginCardSlot> slots = SlotList(list);
            if (index < 0 || index >= slots.Count || slots[index] == null)
                return;
            SetHighlightedSlot.Invoke(slots[index], new object[] { on });
        }

        private void Cancel()
        {
            if (dragging)
            {
                Highlight(hoverIndex, false);
                Highlight(pressIndex, false);
                if (arrow != null)
                    Destroy(arrow.gameObject);
                if (ghost != null)
                    Destroy(ghost);
                suppressUntilFrame = Time.frameCount + 1;
            }
            ghost = null;
            arrow = null;
            dragging = false;
            anyDragging = false;
            hoverIndex = -1;
            pressIndex = -1;
        }

        private void Swap(int a, int b)
        {
            BookModel book = list.currentunit.bookItem;
            bool fixedDeck = book.IsFixedDeck();
            List<DiceCardXmlInfo> cards = fixedDeck ? book.GetCardListFromCurrentDeck() : CurrentDeck(book)?.GetCardList_nocopy();
            List<DiceCardItemModel> view = ViewList(list);
            if (cards == null || a >= cards.Count || b >= cards.Count || a >= view.Count || b >= view.Count)
                return;
            if (cards[a].id != view[a].GetID() || cards[b].id != view[b].GetID())
            {
                Debug.Log(Initializer.LogPrefix + "Deck editor order does not match the deck; not swapping.");
                return;
            }

            DiceCardXmlInfo temp = cards[a];
            cards[a] = cards[b];
            cards[b] = temp;

            if (fixedDeck)
                FixedDeckOrderStore.Set(book, cards.ConvertAll(c => c.id));
            list.changed = true;
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Apply);
            if (list.rootPanel != null)
                list.rootPanel.RefreshAll();
            else
                list.RefreshAll();
        }
    }
}
