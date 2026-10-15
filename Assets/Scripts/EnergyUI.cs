using UnityEngine;
using UnityEngine.UI;

// Shows energy as a row of "pips" plus an optional progress bar for button-press charging.
// To use your own art later: assign your sprites below (or just swap the Images' sprites).
public class EnergyUI : MonoBehaviour
{
    [SerializeField] private EnergySystem energySystem;

    [Header("Energy pips (one Image per energy point, left to right)")]
    [SerializeField] private Image[] pips;

    [Header("Optional: your own sprites (leave empty to use colors)")]
    [SerializeField] private Sprite fullSprite;
    [SerializeField] private Sprite emptySprite;

    [Header("Fallback colors (used when no sprite is set)")]
    [SerializeField] private Color fullColor = new Color(0.2f, 0.8f, 1f);
    [SerializeField] private Color emptyColor = new Color(0.15f, 0.15f, 0.2f, 0.8f);

    [Header("Optional: charge progress bar (Image Type = Filled)")]
    [SerializeField] private Image progressFill;

    private void OnEnable()
    {
        if (energySystem == null) return;
        energySystem.OnEnergyChanged += Refresh;
        Refresh(energySystem.CurrentEnergy, energySystem.PressProgress);
    }

    private void OnDisable()
    {
        if (energySystem != null)
            energySystem.OnEnergyChanged -= Refresh;
    }

    private void Refresh(int energy, int pressProgress)
    {
        for (int i = 0; i < pips.Length; i++)
        {
            bool isFull = i < energy;
            pips[i].sprite = isFull ? fullSprite : emptySprite;
            pips[i].color = (isFull ? fullSprite : emptySprite) != null
                ? Color.white
                : (isFull ? fullColor : emptyColor);
        }

        if (progressFill != null)
        {
            bool atCap = energy >= energySystem.MaxEnergy;
            progressFill.fillAmount = atCap
                ? 1f
                : (float)pressProgress / energySystem.PressesPerEnergy;
        }
    }
}
