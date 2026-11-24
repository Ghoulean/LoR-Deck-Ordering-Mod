using System;
using System.Collections.Generic;
using System.Linq;
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
            {
                __instance.gameObject.AddComponent<DeckSwapController>();
            }
        }
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerClick))]
    internal static class SuppressRemoveAfterDrag
    {
        private static bool Prefix()
        {
            return !DeckSwapController.ShouldSuppressClick;
        }
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerEnter))]
    internal static class SuppressDeckSlotEnterWhileDragging
    {
        private static bool Prefix()
        {
            return !DeckSwapController.IsDragging;
        }
    }

    [HarmonyPatch(typeof(UIEquipDeckCardSlot), nameof(UIEquipDeckCardSlot.OnPointerExit))]
    internal static class SuppressDeckSlotExitWhileDragging
    {
        private static bool Prefix()
        {
            return !DeckSwapController.IsDragging;
        }
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

        private static bool Prefix()
        {
            return !DeckSwapController.IsDragging;
        }
    }

    internal class DeckSwapController : MonoBehaviour
    {
        private const float LongPressSeconds = 0.35f;
        private const float DragThresholdPixels = 12f;
        private const float GhostAlpha = 0.45f;

        private static readonly Func<UIOriginCardList, List<UIOriginCardSlot>> SlotList =
            Reflect.FieldGetter<UIOriginCardList, List<UIOriginCardSlot>>("slotList");
        private static readonly Func<UIOriginCardList, List<DiceCardItemModel>> ViewList =
            Reflect.FieldGetter<UIOriginCardList, List<DiceCardItemModel>>("currentviewCardList");
        private static readonly MethodInfo SetHighlightedSlot = Reflect.Method(typeof(UIOriginCardSlot), "SetHighlightedSlot");

        private sealed class Press
        {
            internal readonly int Index;
            internal readonly float Time;
            internal readonly Vector2 Position;

            internal Press(int index, float time, Vector2 position)
            {
                Index = index;
                Time = time;
                Position = position;
            }

            internal bool ShouldStartDrag(float now, Vector2 mouse)
            {
                return now - Time >= LongPressSeconds || (mouse - Position).magnitude >= DragThresholdPixels;
            }
        }

        private sealed class Drag
        {
            internal readonly GameObject Ghost;
            internal readonly Vector3 GhostOffset;
            internal readonly Canvas RootCanvas;
            internal readonly Camera Camera;
            internal readonly SwapArrow Arrow;
            internal readonly int HoverIndex;

            internal Drag(GameObject ghost, Vector3 ghostOffset, Canvas rootCanvas, Camera camera, SwapArrow arrow, int hoverIndex)
            {
                Ghost = ghost;
                GhostOffset = ghostOffset;
                RootCanvas = rootCanvas;
                Camera = camera;
                Arrow = arrow;
                HoverIndex = hoverIndex;
            }

            internal Drag WithHover(int hoverIndex)
            {
                return new Drag(Ghost, GhostOffset, RootCanvas, Camera, Arrow, hoverIndex);
            }
        }

        private sealed class Sorting
        {
            internal readonly int LayerId;
            internal readonly int Order;

            internal Sorting(int layerId, int order)
            {
                LayerId = layerId;
                Order = order;
            }

            private int LayerValue => SortingLayer.GetLayerValueFromID(LayerId);

            internal bool IsAbove(Sorting other)
            {
                return LayerValue > other.LayerValue || (LayerValue == other.LayerValue && Order > other.Order);
            }

            internal Sorting Raised(int amount)
            {
                return new Sorting(LayerId, Math.Min(Order + amount, short.MaxValue - 1));
            }
        }

        private static bool anyDragging;
        private static int suppressUntilFrame = -1;

        internal static bool ShouldSuppressClick => anyDragging || Time.frameCount <= suppressUntilFrame;
        internal static bool IsDragging => anyDragging;

        private UIEquipDeckCardList list;
        private Press press;
        private Drag drag;

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
            {
                return;
            }

            Vector2 mouse = Input.mousePosition;

            if (Input.GetMouseButtonDown(0))
            {
                Cancel();
                press = NewPress(SlotIndexAt(mouse), mouse);
                return;
            }

            if (press == null)
            {
                return;
            }

            if (Input.GetMouseButton(0))
            {
                Hold(mouse);
            }
            else
            {
                Release(mouse);
            }
        }

        private Press NewPress(int index, Vector2 mouse)
        {
            return index >= 0 && CanEdit(list) ? new Press(index, Time.unscaledTime, mouse) : null;
        }

        private void Hold(Vector2 mouse)
        {
            if (drag != null)
            {
                drag = UpdateDrag(drag, press.Index, mouse);
                return;
            }
            if (!press.ShouldStartDrag(Time.unscaledTime, mouse))
            {
                return;
            }

            drag = BeginDrag(press.Index, mouse);
            anyDragging = drag != null;
            if (drag == null)
            {
                press = null;
            }
        }

        private void Release(Vector2 mouse)
        {
            int source = press.Index;
            bool wasDragging = drag != null;
            int target = wasDragging ? SlotIndexAt(mouse) : -1;
            Cancel();
            if (IsSwap(source, target) && CanEdit(list))
            {
                Swap(source, target);
            }

            if (wasDragging)
            {
                Highlight(SlotIndexAt(mouse), true);
            }
        }

        private static bool IsSwap(int source, int target)
        {
            return target >= 0 && target != source;
        }

        private static int TargetIndex(int hover, int source)
        {
            return hover == source ? -1 : hover;
        }

        private static bool CanEdit(UIEquipDeckCardList list)
        {
            return CanEdit(list, UI.UIController.Instance.CurrentUIPhase);
        }

        private static bool CanEdit(UIEquipDeckCardList list, UIPhase phase)
        {
            return list.currentunit?.bookItem != null
            && !list.IsDisabled
            && (phase == UIPhase.Librarian_CardList || phase == UIPhase.BattleSetting)
            && !(list.rootPanel != null && list.rootPanel.IsLockBattleBluePrimary())
            && !list.currentunit.bookItem.IsLockByBluePrimary();
        }

        private int SlotIndexAt(Vector2 screen)
        {
            List<UIOriginCardSlot> slots = SlotList(list);
            return Enumerable.Range(0, Math.Min(ViewList(list).Count, slots.Count))
                .Where(i => IsHit(slots[i], screen))
                .DefaultIfEmpty(-1)
                .First();
        }

        private static bool IsHit(UIOriginCardSlot slot, Vector2 screen)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy || slot.CardModel == null)
            {
                return false;
            }

            RectTransform rt = HitRect(slot);
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screen, CameraFor(slot));
        }

        private static RectTransform HitRect(UIOriginCardSlot slot)
        {
            return (slot.selectable != null ? slot.selectable.transform : slot.transform) as RectTransform;
        }

        private static Camera CameraFor(Component c)
        {
            Canvas canvas = c.GetComponentInParent<Canvas>();
            return canvas == null ? null : CameraOf(canvas.rootCanvas);
        }

        private static Camera CameraOf(Canvas root)
        {
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private static Vector3 ScreenToWorld(Canvas root, Camera camera, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)root.transform, screen, camera, out Vector3 world);
            return world;
        }

        private Drag BeginDrag(int index, Vector2 mouse)
        {
            UIOriginCardSlot slot = SlotList(list)[index];
            Canvas canvas = slot.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            Canvas root = canvas.rootCanvas;
            Camera camera = CameraFor(slot);
            HideAllCardPreviews();

            Highlight(index, true);

            SwapArrow arrow = SwapArrow.Create(root.transform);
            GameObject ghost = CreateGhost(slot, root);

            Sorting top = TopSorting(root);
            DrawOnTop(arrow.gameObject, top);
            DrawOnTop(ghost, top.Raised(1));

            Drag started = new Drag(ghost, slot.transform.position - ScreenToWorld(root, camera, mouse), root, camera, arrow, -1);
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Over);
            return UpdateDrag(started, index, mouse);
        }

        private static GameObject CreateGhost(UIOriginCardSlot slot, Canvas root)
        {
            GameObject ghost = Instantiate(slot.gameObject, root.transform, true);
            ghost.name = "DeckOrdering_DragGhost";
            ghost.transform.SetAsLastSibling();
            foreach (Selectable s in ghost.GetComponentsInChildren<Selectable>(true))
            {
                Destroy(s);
            }

            foreach (EventTrigger t in ghost.GetComponentsInChildren<EventTrigger>(true))
            {
                Destroy(t);
            }

            foreach (UIOriginCardSlot s in ghost.GetComponentsInChildren<UIOriginCardSlot>(true))
            {
                s.enabled = false;
            }

            CanvasGroup existing = ghost.GetComponent<CanvasGroup>();
            CanvasGroup group = existing != null ? existing : ghost.AddComponent<CanvasGroup>();
            group.alpha = GhostAlpha;
            group.blocksRaycasts = false;
            group.interactable = false;
            return ghost;
        }

        private static Sorting TopSorting(Canvas root)
        {
            return FindObjectsOfType<Canvas>()
                .Where(c => c.isActiveAndEnabled && (c.isRootCanvas || c.overrideSorting))
                .Where(c => c.rootCanvas.renderMode == root.renderMode && c.rootCanvas.worldCamera == root.worldCamera)
                .Select(c => new Sorting(c.sortingLayerID, c.sortingOrder))
                .Aggregate(new Sorting(root.sortingLayerID, root.sortingOrder), (best, candidate) => candidate.IsAbove(best) ? candidate : best)
                .Raised(10);
        }

        private static void DrawOnTop(GameObject go, Sorting sorting)
        {
            Canvas existing = go.GetComponent<Canvas>();
            Canvas canvas = existing != null ? existing : go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = sorting.LayerId;
            canvas.sortingOrder = sorting.Order;
        }

        private void HideAllCardPreviews()
        {
            list.HideDetailSlotByInventory();
            foreach (UIInvenCardListScroll inventory in FindObjectsOfType<UIInvenCardListScroll>())
            {
                inventory.HideDetailSlotByInventory();
            }

            foreach (UIEquipCardList equipped in FindObjectsOfType<UIEquipCardList>())
            {
                equipped.HideDetailSlotByInventory();
            }
        }

        private Drag UpdateDrag(Drag current, int source, Vector2 mouse)
        {
            if (current.Ghost != null)
            {
                current.Ghost.transform.position = ScreenToWorld(current.RootCanvas, current.Camera, mouse) + current.GhostOffset;
            }

            int hover = TargetIndex(SlotIndexAt(mouse), source);
            if (hover == current.HoverIndex)
            {
                return current;
            }

            Highlight(current.HoverIndex, false);
            if (hover < 0)
            {
                if (current.Arrow != null)
                {
                    current.Arrow.Hide();
                }

                return current.WithHover(hover);
            }

            Highlight(hover, true);
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Over);
            if (current.Arrow != null)
            {
                current.Arrow.Show(SlotCenter(source), SlotCenter(hover));
            }

            return current.WithHover(hover);
        }

        private Vector3 SlotCenter(int index)
        {
            return Center(SlotList(list)[index]);
        }

        private static Vector3 Center(UIOriginCardSlot slot)
        {
            RectTransform rt = HitRect(slot);
            return rt != null ? rt.TransformPoint(rt.rect.center) : slot.transform.position;
        }

        private void Highlight(int index, bool on)
        {
            List<UIOriginCardSlot> slots = SlotList(list);
            if (index < 0 || index >= slots.Count || slots[index] == null)
            {
                return;
            }

            SetHighlightedSlot.Invoke(slots[index], new object[] { on });
        }

        private void Cancel()
        {
            if (drag != null)
            {
                Highlight(drag.HoverIndex, false);
                Highlight(press?.Index ?? -1, false);
                if (drag.Arrow != null)
                {
                    Destroy(drag.Arrow.gameObject);
                }

                if (drag.Ghost != null)
                {
                    Destroy(drag.Ghost);
                }

                suppressUntilFrame = Time.frameCount + 1;
            }
            drag = null;
            press = null;
            anyDragging = false;
        }

        private static bool InRange(int a, int b, params int[] counts)
        {
            return counts.All(count => a < count && b < count);
        }

        private static bool Matches(IReadOnlyList<DiceCardXmlInfo> cards, IReadOnlyList<DiceCardItemModel> view, int index)
        {
            return cards[index].id == view[index].GetID();
        }

        private static IReadOnlyList<T> Swapped<T>(IReadOnlyList<T> items, int a, int b)
        {
            return items.Select((item, i) => i == a ? items[b] : i == b ? items[a] : item).ToList();
        }

        private void Swap(int a, int b)
        {
            BookModel book = list.currentunit.bookItem;
            bool fixedDeck = book.IsFixedDeck();
            List<DiceCardXmlInfo> cards = fixedDeck ? book.GetCardListFromCurrentDeck() : BookCardLists.CurrentDeck(book)?.GetCardList_nocopy();
            List<DiceCardItemModel> view = ViewList(list);
            if (cards == null || !InRange(a, b, cards.Count, view.Count))
            {
                return;
            }

            if (!Matches(cards, view, a) || !Matches(cards, view, b))
            {
                Debug.Log(Initializer.LogPrefix + "Deck editor order does not match the deck; not swapping.");
                return;
            }

            if (fixedDeck)
            {
                FixedDeckOrderStore.Swap(book, cards, a, b);
            }
            else
            {
                IReadOnlyList<DiceCardXmlInfo> swapped = Swapped(cards, a, b);
                cards[a] = swapped[a];
                cards[b] = swapped[b];
            }

            list.changed = true;
            UISoundManager.instance.PlayEffectSound(UISoundType.Card_Apply);
            if (list.rootPanel != null)
            {
                list.rootPanel.RefreshAll();
            }
            else
            {
                list.RefreshAll();
            }
        }
    }
}
