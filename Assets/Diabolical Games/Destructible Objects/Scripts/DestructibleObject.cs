using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//Made by Rajendra Abhinaya, 2023

namespace DiabolicalGames
{
    public class DestructibleObject : MonoBehaviour
    {
        enum DebrisAmount
        {
            Low,
            Medium,
            High,
            Random
        }

        enum DespawnType
        {
            None,
            Timed,
            DistanceFromPlayer
        }

        [System.Serializable]
        struct DebrisPrefab
        {
            public string name;
            public GameObject prefab;
        }

        [Header("Debris")]
        [SerializeField, Tooltip("List of debris prefabs that will be spawned when the object is destroyed. Editing the list is not recommended")]
        private List<DebrisPrefab> debrisPrefabs = new List<DebrisPrefab>();

        [SerializeField, Tooltip("Amount of debris(Gameobjects) that will be spawned when the object breaks")]
        private DebrisAmount debrisAmount = new DebrisAmount();

        [SerializeField, Tooltip("Force required to break the object. Compared against collision speed for normal impacts, and against the force value the player sends (walking into it or attacking)")]
        private float forceRequired;

        [Header("Player Interaction")]
        [SerializeField, Tooltip("If on, plain collisions with the player are ignored and only the player's push/attack force counts. This makes the player's force settings the only thing that decides whether they break it")]
        private bool ignorePlayerCollisions = true;

        [SerializeField, Tooltip("Tag on the player object, used by Ignore Player Collisions")]
        private string playerTag = "Player";

        [SerializeField, Tooltip("Extra speed given to the debris in the direction of the hit when broken by the player")]
        private float debrisLaunchSpeed = 4f;

        [Header("Chain Reactions")]
        [SerializeField, Tooltip("If on, flying debris from other destroyed objects can't break this object. Prevents chain reactions")]
        private bool ignoreDebrisCollisions = true;

        [Header("Despawning")]
        [SerializeField, Tooltip("Despawn mode used for despawning the debris created by destroying the object\nNone: debris will not be despawned\nTimed: debris will despawned after a certain time\nDistance from Player: debris will despawn when the player moves a certain distance away from the debris")]
        private DespawnType despawnType = new DespawnType();

        [SerializeField, Range(0, 100), Tooltip("Percentage of debris objects that will be despawned")]
        private int despawnPercentage;

        [SerializeField, Tooltip("Time in seconds before debris will despawn when using the Timed despawn mode")]
        private float despawnTime;

        [SerializeField, Tooltip("Player gameobject for when using the Distance from Player despawn mode")]
        private GameObject player;

        [SerializeField, Tooltip("Distance between debris and player before debris despawns when using the Distance from Player despawn mode")]
        private float distanceFromPlayer;

        [Header("Audio")]
        [SerializeField, Tooltip("List of audio clips that will be played when the object breaks. Audio clips are selected randomly from the list")]
        private List<AudioClip> audioClips = new List<AudioClip>();

        [SerializeField, Tooltip("Volume of the audio clip when played"), Range(0f, 1f)]
        private float volume;

        [SerializeField, Tooltip("Amount of variation in the volume of each audio clip played"), Range(0f, 0.2f)]
        private float volumeVariation;

        [SerializeField, Tooltip("Amount of variation in the pitch volume of each audio clip played"), Range(0f, 0.5f)]
        private float pitchVariation;

        private GameObject debris;
        private new Rigidbody rigidbody;

        //Original scale of the debris prefab, so it can be combined with the object's scale instead of being overwritten
        private Vector3 debrisBaseScale;

        //Prevents breaking twice (e.g. several physics steps or hits before Destroy takes effect)
        private bool isBroken;

        /// <summary>
        /// Called by the player when walking into this object or attacking it.
        /// </summary>
        public void ApplyForce(float force, Vector3 point, Vector3 direction)
        {
            if (force >= forceRequired)
            {
                Break(direction * debrisLaunchSpeed);
            }
        }

        public void Break()
        {
            Break(Vector3.zero);
        }

        /// <param name="extraVelocity">Velocity added to every debris piece, e.g. to throw it away from a hit.</param>
        public void Break(Vector3 extraVelocity)
        {
            if (isBroken) return;
            isBroken = true;

            Vector3 baseVelocity = (rigidbody != null ? rigidbody.linearVelocity : Vector3.zero) + extraVelocity;
            float velocityMagnitude = baseVelocity.magnitude;

            //Activates the debris object and sets its position and rotation to match the object's
            debris.transform.position = transform.position;
            debris.transform.rotation = transform.rotation;
            //Multiplies the prefab's own scale by the object's scale so changes to the prefab scale are kept
            debris.transform.localScale = Vector3.Scale(debrisBaseScale, transform.localScale);
            debris.SetActive(true);

            //Applies force to the debris based on the velocity of the object (plus the hit, if any)
            for (int i = 0; i < debris.transform.childCount; i++)
            {
                Rigidbody debrisRigidbody = debris.transform.GetChild(i).GetComponent<Rigidbody>();
                if (debrisRigidbody == null) continue;

                Vector3 randomise = new Vector3(Random.Range(0f, velocityMagnitude), Random.Range(0f, velocityMagnitude), Random.Range(0f, velocityMagnitude)) / 2;
                debrisRigidbody.linearVelocity = baseVelocity + randomise;
            }

            //Sends variable values to the debris
            AudioClip clip = audioClips.Count > 0 ? audioClips[Random.Range(0, audioClips.Count)] : null;
            debris.GetComponent<Despawn>().SetVariables(despawnPercentage, despawnTime, distanceFromPlayer, player, clip, volume, volumeVariation, pitchVariation);

            //Activates the despawning mechanism of the debris
            switch (despawnType)
            {
                case DespawnType.Timed:
                    debris.GetComponent<Despawn>().BeginCoroutine("Timed");
                    break;
                case DespawnType.DistanceFromPlayer:
                    debris.GetComponent<Despawn>().BeginCoroutine("Distance from Player");
                    break;
            }

            //Destroys the game object
            Destroy(gameObject);
        }

        void Start()
        {
            rigidbody = GetComponent<Rigidbody>();

            //Instantiates the debris prefab based on the amount chosen and then disables the debris object
            switch (debrisAmount)
            {
                case DebrisAmount.Low:
                    debris = Instantiate(debrisPrefabs[0].prefab, transform.position, Quaternion.identity);
                    break;
                case DebrisAmount.Medium:
                    debris = Instantiate(debrisPrefabs[1].prefab, transform.position, Quaternion.identity);
                    break;
                case DebrisAmount.High:
                    debris = Instantiate(debrisPrefabs[2].prefab, transform.position, Quaternion.identity);
                    break;
                case DebrisAmount.Random:
                    debris = Instantiate(debrisPrefabs[Random.Range(0, 3)].prefab, transform.position, Quaternion.identity);
                    break;
                default:
                    debris = Instantiate(debrisPrefabs[0].prefab, transform.position, Quaternion.identity);
                    break;
            }

            //Stores the prefab's scale before anything changes it
            debrisBaseScale = debris.transform.localScale;
            debris.SetActive(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            //The player breaks things through ApplyForce instead, so its force settings are respected
            if (ignorePlayerCollisions && collision.gameObject.CompareTag(playerTag))
                return;

            //Debris pieces belong to a debris root that has a Despawn component
            if (ignoreDebrisCollisions && collision.collider.GetComponentInParent<Despawn>() != null)
                return;

            if (collision.relativeVelocity.magnitude > forceRequired)
            {
                Break();
            }
        }
    }
}