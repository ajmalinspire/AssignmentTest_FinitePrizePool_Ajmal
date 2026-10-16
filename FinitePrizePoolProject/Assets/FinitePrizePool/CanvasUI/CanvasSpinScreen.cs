using System;
using System.Collections.Generic;
using System.Linq;
using FinitePrizePool.Engine.Core;
using FinitePrizePool.Engine.Persistence;
using FinitePrizePool.Engine.Service;
using FinitePrizePool.Game;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FinitePrizePool.CanvasUI
{
    /// <summary>
    /// The game client (§5) as a Unity Canvas (uGUI + TextMeshPro), laid out in the GameCanvas prefab: player, mode
    /// and bet selectors, the spin button, the result and the balance. It only ever calls IDealService:
    ///   Start:  GetUnshownResult (show a result the last session never showed), PrepareSlot
    ///   Update: GetSlot / GetPlayer to refresh the controls; when the reveal finishes, MarkResultShown
    ///   Spin:   Spin(player, mode, bet). The result is saved before this returns; then the reveal plays.
    /// The player, mode and bet buttons already in the prefab are reused in order. A button is copied from
    /// <see cref="choiceButtonPrefab"/> only if the config has more choices than the prefab, and spare ones are hidden.
    /// </summary>
    public sealed class CanvasSpinScreen : MonoBehaviour
    {
        [Header("Engine (the GameRoot in the scene)")]
        [SerializeField] private GameRoot gameRoot;

        [Header("Selectors: a container for each group, and a button prefab for extra choices")]
        [SerializeField] private Button choiceButtonPrefab;
        [SerializeField] private Transform playerButtons;
        [SerializeField] private Transform modeButtons;
        [SerializeField] private Transform betButtons;

        [Header("Balance, spin button and status line")]
        [SerializeField] private TMP_Text balanceText;
        [SerializeField, Tooltip("Optional: the selected player's own spin count.")]
        private TMP_Text spinCountText;
        [SerializeField] private Button spinButton;
        [SerializeField] private TMP_Text spinButtonLabel;
        [SerializeField] private TMP_Text statusText;

        [Header("Result panel")]
        [SerializeField] private TMP_Text headlineText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private TMP_Text noteText;

        [Header("Debug panel (Editor and development builds only; F1 hides it)")]
        [SerializeField] private GameObject debugPanel;
        [SerializeField] private TMP_Text debugText;

        [Header("Look and timing")]
        [SerializeField, Tooltip("How long the spin animation runs before the result is revealed.")]
        private float spinSeconds = 1.5f;
        [SerializeField] private Color selectedColor = new Color(1f, 0.82f, 0.32f);
        [SerializeField] private Color normalColor = new Color(0.22f, 0.25f, 0.33f);
        [SerializeField] private Color selectedLabelColor = new Color(0.07f, 0.08f, 0.11f);
        [SerializeField] private Color normalLabelColor = new Color(0.93f, 0.94f, 0.97f);
        [SerializeField] private Color winColor = new Color(1f, 0.82f, 0.32f);
        [SerializeField] private Color lossColor = new Color(0.62f, 0.66f, 0.74f);
        [SerializeField] private Color warningColor = new Color(1f, 0.55f, 0.45f);
        [SerializeField] private Color noteColor = new Color(0.62f, 0.66f, 0.74f);

        private struct Choice
        {
            public Button Button;
            public TMP_Text Label;
        }

        private IDealService _service;
        private SpinPresenter _presenter;
        private readonly List<Choice> _playerChoices = new List<Choice>();
        private readonly List<string> _playerIds = new List<string>();
        private readonly List<Choice> _modeChoices = new List<Choice>();
        private readonly List<Choice> _betChoices = new List<Choice>();
        private string _playerId;
        private int _modeIndex;
        private int _betIndex;
        private string _message;
        private bool _started;

        private string Mode => _service.Modes[_modeIndex];
        private long Bet => _service.BetLevelsCents[_betIndex];

        // ---- lifecycle --------------------------------------------------------------------------------------

        private void Start()
        {
            var missing = MissingReferences();
            if (missing != null)
            {
                Debug.LogError("[FinitePrizePool] CanvasSpinScreen is not wired: " + missing + ". Rebuild with Tools > Finite Prize Pool > Build Canvas UI.", this);
                enabled = false;
                return;
            }

            _presenter = new SpinPresenter(spinSeconds);
            _service = gameRoot != null ? gameRoot.Service : null;
            if (_service == null)
            {
                statusText.text = "The game could not start: " + (gameRoot != null ? gameRoot.StartupError : "no GameRoot assigned");
                statusText.color = warningColor;
                spinButton.interactable = false;
                return;
            }

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            if (debugPanel != null) debugPanel.SetActive(false); // never in a release build
#endif
            BuildChoices();
            spinButton.onClick.AddListener(OnSpinClicked);
            ShowIdle();

            _playerId = _service.GetPlayers().First().Id;
            _betIndex = Mathf.Max(0, _service.BetLevelsCents.ToList().IndexOf(100));
            RestoreUnshownResult();
            _service.PrepareSlot(Mode, Bet);
            _started = true;
        }

        /// <summary>The required references left unassigned in the Inspector, or null if all are set.</summary>
        private string MissingReferences()
        {
            var missing = new List<string>();
            void Need(UnityEngine.Object value, string name)
            {
                if (value == null) missing.Add(name);
            }
            Need(gameRoot, nameof(gameRoot));
            Need(choiceButtonPrefab, nameof(choiceButtonPrefab));
            Need(playerButtons, nameof(playerButtons));
            Need(modeButtons, nameof(modeButtons));
            Need(betButtons, nameof(betButtons));
            Need(balanceText, nameof(balanceText));
            Need(spinButton, nameof(spinButton));
            Need(spinButtonLabel, nameof(spinButtonLabel));
            Need(statusText, nameof(statusText));
            Need(headlineText, nameof(headlineText));
            Need(detailText, nameof(detailText));
            Need(noteText, nameof(noteText));
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        private void Update()
        {
            if (!_started) return;
            if (_presenter.Tick(Time.unscaledDeltaTime))
            {
                ShowResult(_presenter.Current, _presenter.Restored);
                _service.MarkResultShown(_presenter.Current.SpinId); // only now does the player count as having seen it
            }
            HandleKeys();
            RefreshControls();
        }

        private void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space)) OnSpinClicked();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F1) && debugPanel != null) debugPanel.SetActive(!debugPanel.activeSelf);
#endif
#endif
        }

        // ---- selection --------------------------------------------------------------------------------------

        private void BuildChoices()
        {
            var players = _service.GetPlayers();
            _playerIds.AddRange(players.Select(p => p.Id));
            _playerChoices.AddRange(BindChoices(playerButtons, players.Select(p => p.Name).ToList(), i => SelectPlayer(_playerIds[i])));
            _modeChoices.AddRange(BindChoices(modeButtons, _service.Modes.ToList(), i => SelectSlot(i, _betIndex)));
            _betChoices.AddRange(BindChoices(betButtons, _service.BetLevelsCents.Select(Money.Format).ToList(), i => SelectSlot(_modeIndex, i)));
        }

        /// <summary>Uses the container's buttons in order, adds copies of the prefab if it has too few, hides spares.</summary>
        private List<Choice> BindChoices(Transform container, IReadOnlyList<string> labels, Action<int> onClick)
        {
            var existing = new List<Button>();
            foreach (Transform child in container)
            {
                var button = child.GetComponent<Button>();
                if (button != null) existing.Add(button);
            }

            var choices = new List<Choice>();
            for (var i = 0; i < labels.Count; i++)
            {
                var button = i < existing.Count ? existing[i] : Instantiate(choiceButtonPrefab, container);
                button.gameObject.SetActive(true);
                button.onClick.RemoveAllListeners();
                var index = i;
                button.onClick.AddListener(() => onClick(index));
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = labels[i];
                choices.Add(new Choice { Button = button, Label = label });
            }
            for (var i = labels.Count; i < existing.Count; i++) existing[i].gameObject.SetActive(false);
            return choices;
        }

        private void SelectPlayer(string playerId)
        {
            _playerId = playerId; // every player shares the deal, so the deal carries on unchanged
            _message = null;
        }

        private void SelectSlot(int modeIndex, int betIndex)
        {
            _modeIndex = modeIndex;
            _betIndex = betIndex;
            _message = null;
            _service.PrepareSlot(Mode, Bet); // returns at once; the deal is built in the background if needed
        }

        // ---- spinning ---------------------------------------------------------------------------------------

        private void OnSpinClicked()
        {
            // Space and a click on the focused button can land in the same frame: only one may spin.
            if (_presenter.IsSpinning || !_service.GetSlot(Mode, Bet).CanSpin) return;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            // Charged, ticket taken, award credited and record saved inside this call, before any animation.
            var outcome = _service.Spin(_playerId, Mode, Bet);
            if (!outcome.Accepted)
            {
                _message = outcome.Message;
                return;
            }
            _message = null;
            _presenter.Begin(outcome.Record, restored: false);
            detailText.text = "";
            noteText.text = "";
            // Start a reel animation, sound or Animator trigger here. The reveal happens in Update when the
            // presenter's time is up; to drive it from an animation instead, call MarkResultShown at its end.
        }

        private void RestoreUnshownResult()
        {
            var unshown = _service.GetUnshownResult();
            if (unshown == null) return;
            _playerId = unshown.PlayerId;
            _modeIndex = Mathf.Max(0, _service.Modes.ToList().IndexOf(unshown.Mode));
            _betIndex = Mathf.Max(0, _service.BetLevelsCents.ToList().IndexOf(unshown.BetCents));
            _presenter.Begin(unshown, restored: true); // revealed on the next frame, without a new ticket
        }

        // ---- drawing ----------------------------------------------------------------------------------------

        private void RefreshControls()
        {
            var spinning = _presenter.IsSpinning;
            var slot = _service.GetSlot(Mode, Bet);

            for (var i = 0; i < _playerChoices.Count; i++) Mark(_playerChoices[i], _playerIds[i] == _playerId, !spinning);
            for (var i = 0; i < _modeChoices.Count; i++) Mark(_modeChoices[i], i == _modeIndex, !spinning);
            for (var i = 0; i < _betChoices.Count; i++) Mark(_betChoices[i], i == _betIndex, !spinning);

            spinButton.interactable = !spinning && slot.CanSpin;
            spinButtonLabel.text = spinning ? "Spinning..." : "SPIN   " + Money.Format(Bet);
            balanceText.text = "Balance   " + Money.Format(DisplayedBalance());
            if (spinCountText != null) spinCountText.text = PrizeText.SpinCount(_service.GetPlayer(_playerId)?.SpinCount ?? 0);

            if (!slot.CanSpin)
            {
                statusText.text = slot.BuildError != null
                    ? "This deal was rejected: " + slot.BuildError
                    : "Preparing the deal for " + Mode + " " + Money.Format(Bet) + "...";
                statusText.color = slot.BuildError != null ? warningColor : noteColor;
            }
            else
            {
                statusText.text = _message ?? "";
                statusText.color = warningColor;
            }

            if (spinning)
            {
                headlineText.text = "Spinning" + new string('.', 1 + (int)(Time.unscaledTime * 4f) % 3);
                headlineText.color = lossColor;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugText != null && debugPanel != null && debugPanel.activeSelf)
                debugText.text = "Deal ID" + Column + (slot.ActiveDealId ?? "-")
                                 + "\nDeal state" + Column + (slot.ActiveState?.ToString() ?? "-")
                                 + "\nConfig version" + Column + (slot.ActiveConfigVersion ?? "-")
                                 + "\nSpins played" + Column + slot.SpinsPlayed
                                 + "\nNext deal" + Column + NextDeal(slot);
#endif
        }

        private const string Column = "<pos=36%>"; // TextMeshPro tag: the value column of the debug rows

        private static string NextDeal(SlotView slot)
        {
            if (slot.ReadyDealId != null) return slot.ReadyDealId + " (Ready, " + slot.ReadyConfigVersion + ")";
            return slot.IsBuilding ? "building..." : "-";
        }

        private void Mark(Choice choice, bool selected, bool interactable)
        {
            choice.Button.interactable = interactable;
            choice.Button.image.color = selected ? selectedColor : normalColor;
            if (choice.Label != null) choice.Label.color = selected ? selectedLabelColor : normalLabelColor;
        }

        /// <summary>While the reels spin, the balance shows the wager taken but not the award, which comes with the reveal.</summary>
        private long DisplayedBalance()
        {
            var spin = _presenter.Current;
            if (_presenter.IsSpinning && spin != null && spin.PlayerId == _playerId) return spin.BalanceBeforeCents - spin.WagerCents;
            return _service.GetPlayer(_playerId)?.BalanceCents ?? 0;
        }

        private void ShowIdle()
        {
            headlineText.text = "";
            detailText.text = "";
            noteText.text = "Pick a player, a mode and a bet, then press SPIN (or Space).";
        }

        private void ShowResult(SpinRecord spin, bool restored)
        {
            headlineText.text = PrizeText.Headline(spin);
            headlineText.color = spin.AwardCents > 0 ? winColor : lossColor;
            detailText.text = PrizeText.Detail(spin);
            var who = _service.GetPlayer(spin.PlayerId)?.Name ?? spin.PlayerId;
            noteText.text = (restored ? "Result of your last spin: the game closed before it was shown.\n" : "")
                            + PrizeText.Footer(spin, who);
        }
    }
}
