using System;
using UnityEngine;
using UnityEngine.Events;

public class EnergySystem : MonoBehaviour
{
    [Header("Energy Settings")]
    [SerializeField] private int maxEnergy = 2;
    [SerializeField] private int pressesPerEnergy = 5;   // button presses needed for 1 energy
    [SerializeField] private int moveCost = 1;           // energy cost per move

    [Header("Keys")]
    [SerializeField] private KeyCode chargeKey = KeyCode.Space;
    [SerializeField] private KeyCode attackKey = KeyCode.Q;
    [SerializeField] private KeyCode aoeAttackKey = KeyCode.E;
    [SerializeField] private KeyCode radarKey = KeyCode.R;

    [Header("Moves (hook your existing methods up here)")]
    public UnityEvent onAttack;
    public UnityEvent onAoeAttack;
    public UnityEvent onRadar;

    [Header("Input")]
    [SerializeField] private bool useBuiltInKeyInput = true; // turn off if you call the public methods yourself

    public int MaxEnergy => maxEnergy;
    public int PressesPerEnergy => pressesPerEnergy;
    public int CurrentEnergy { get; private set; }
    public int PressProgress { get; private set; }       // presses toward the next energy

    // (currentEnergy, pressProgress) - hook your UI up to this
    public event Action<int, int> OnEnergyChanged;

    private void Update()
    {
        if (!useBuiltInKeyInput) return;

        if (Input.GetKeyDown(chargeKey)) RegisterChargePress();
        if (Input.GetKeyDown(attackKey)) UseMove(onAttack);
        if (Input.GetKeyDown(aoeAttackKey)) UseMove(onAoeAttack);
        if (Input.GetKeyDown(radarKey)) UseMove(onRadar);
    }

    private void UseMove(UnityEvent move)
    {
        if (TrySpend(moveCost))
            move?.Invoke();
    }

    // Call this from any input system (old Input, new Input System, UI button...)
    public void RegisterChargePress()
    {
        if (CurrentEnergy >= maxEnergy) return;           // full: presses don't bank

        PressProgress++;
        if (PressProgress >= pressesPerEnergy)
        {
            PressProgress = 0;
            CurrentEnergy++;
        }
        OnEnergyChanged?.Invoke(CurrentEnergy, PressProgress);
    }

    public bool HasEnergy(int cost = 1) => CurrentEnergy >= cost;

    // Returns true and spends energy if the move is affordable
    public bool TrySpend(int cost = 1)
    {
        if (CurrentEnergy < cost) return false;

        CurrentEnergy -= cost;
        OnEnergyChanged?.Invoke(CurrentEnergy, PressProgress);
        return true;
    }
}