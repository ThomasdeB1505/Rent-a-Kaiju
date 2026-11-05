using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Tracks the player's health. Every ForbiddenTarget destroyed removes health.
/// At 0 it activates the lose screens and fires an event.
/// Add this once, to any always-active GameObject in the scene.
/// </summary>
public class LoseManager : MonoBehaviour
{
    public static LoseManager Instance { get; private set; }

    public enum DepletedStyle { Hide, SwapTexture, Tint }

    [Header("Health")]
    [Tooltip("Starting health. Should match the number of Health Slots below.")]
    public int maxHealth = 10;

    [Tooltip("Health lost each time a forbidden object is destroyed. Use 2 or 3 to make the player lose faster.")]
    public int damagePerForbiddenObject = 1;

    [Header("Health Display")]
    [Tooltip("Drag your 10 RawImages here, in order. Each RawImage keeps its own texture.")]
    public RawImage[] healthSlots = new RawImage[10];

    [Tooltip("On = the last slot disappears first. Off = the first slot disappears first.")]
    public bool depleteFromEnd = true;

    [Tooltip("Hide: the RawImage turns off. SwapTexture: shows 'Depleted Texture' instead. Tint: recolors the RawImage. " +
             "If your slots sit in a Layout Group, use SwapTexture or Tint so the layout doesn't collapse.")]
    public DepletedStyle depletedStyle = DepletedStyle.Hide;

    [Tooltip("Used when Depleted Style = SwapTexture (e.g. an empty/cracked version of the icon).")]
    public Texture depletedTexture;

    [Tooltip("Used when Depleted Style = Tint.")]
    public Color depletedColor = new Color(1f, 1f, 1f, 0.15f);

    [Header("UI (optional)")]
    [Tooltip("Auto-updates, e.g. 'Health: 7/10'.")]
    public TMP_Text destroyedCountText;

    [Header("Lose")]
    [Tooltip("Optional panel/screen that gets SetActive(true) the moment health hits 0.")]
    public GameObject loseScreen;
    [Tooltip("Optional second panel/screen, activated at the same time as the first.")]
    public GameObject loseScreen2;
    [Tooltip("Fires every time health changes. Passes the new health (hook up damage sounds, flashes, etc.).")]
    public UnityEvent<int> onHealthChanged;
    [Tooltip("Fires once, the moment health reaches 0.")]
    public UnityEvent onLose;

    public int CurrentHealth => currentHealth;

    private int currentHealth;
    private bool hasLost;
    private Texture[] originalTextures;
    private Color[] originalColors;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple LoseManagers found in scene — destroying the duplicate.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        currentHealth = maxHealth;

        if (healthSlots == null) healthSlots = new RawImage[0];
        if (healthSlots.Length != maxHealth)
            Debug.LogWarning($"LoseManager: Max Health is {maxHealth} but there are {healthSlots.Length} Health Slots.", this);

        // Remember each slot's own look so it can be restored / used as the "full" state.
        originalTextures = new Texture[healthSlots.Length];
        originalColors = new Color[healthSlots.Length];
        for (int i = 0; i < healthSlots.Length; i++)
        {
            if (healthSlots[i] == null) continue;
            originalTextures[i] = healthSlots[i].texture;
            originalColors[i] = healthSlots[i].color;
        }

        UpdateUI();
    }

    public void ReportDestroyed(ForbiddenTarget target)
    {
        if (hasLost) return;

        currentHealth = Mathf.Max(0, currentHealth - Mathf.Max(1, damagePerForbiddenObject));
        UpdateUI();
        onHealthChanged?.Invoke(currentHealth);

        if (currentHealth <= 0)
        {
            hasLost = true;
            Lose();
        }
    }

    void UpdateUI()
    {
        if (destroyedCountText != null)
            destroyedCountText.text = $"Health: {currentHealth}/{maxHealth}";

        UpdateHealthSlots();
    }

    void UpdateHealthSlots()
    {
        for (int i = 0; i < healthSlots.Length; i++)
        {
            var img = healthSlots[i];
            if (img == null) continue;

            bool full = depleteFromEnd
                ? i < currentHealth
                : i >= healthSlots.Length - currentHealth;

            switch (depletedStyle)
            {
                case DepletedStyle.Hide:
                    img.enabled = full;
                    break;

                case DepletedStyle.SwapTexture:
                    img.texture = full ? originalTextures[i] : depletedTexture;
                    img.enabled = full || depletedTexture != null;
                    break;

                case DepletedStyle.Tint:
                    img.enabled = true;
                    img.color = full ? originalColors[i] : depletedColor;
                    break;
            }
        }
    }

    void Lose()
    {
        if (loseScreen != null) loseScreen.SetActive(true);
        if (loseScreen2 != null) loseScreen2.SetActive(true);
        onLose?.Invoke();
    }
}