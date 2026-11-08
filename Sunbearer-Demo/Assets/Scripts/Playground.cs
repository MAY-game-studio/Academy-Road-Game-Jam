using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Sunbearer
{
    [RequireComponent(typeof(SolarTrails), typeof(StardustSpawner))]
    public sealed class Playground : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerController playerPrefab;
        [SerializeField] private SpriteRenderer islandTerrain;
        [SerializeField] private Vector2 spawnPosition;
        [SerializeField] private PolygonCollider2D coastline;
        [SerializeField] private IslandCrossing[] islandCrossings = new IslandCrossing[0];
        [SerializeField] private Vector2[] startingStardust = new Vector2[0];
        [FormerlySerializedAs("pickupSamples")]
        [SerializeField] private PowerUpPickup[] pickupTemplates = new PowerUpPickup[0];

        [Header("Oasis challenges")]
        [SerializeField] private RitualTower[] oasisTowers = new RitualTower[0];
        [SerializeField, Min(0)] private int islandOneShorePath = 1;
        [SerializeField, Min(0)] private int islandTwoShorePath = 2;

        [Header("Progress bars")]
        [SerializeField] private Texture2D healthBarFull;
        [SerializeField] private Texture2D healthBarEmpty;
        [SerializeField] private Texture2D towerBarFull;
        [SerializeField] private Texture2D towerBarEmpty;

        [Header("Demo controls")]
        [SerializeField, Min(1f)] private float damageTestAmount = 40f;

        private const string MainMenuScene = "MainMenu";
        private const int TowerTestEnergy = 25;
        private const float HudMargin = 12f;

        // Pixel bounds in the source art, measured from the top left.
        private static readonly Vector2 ProgressArtSourceSize = new Vector2(2388f, 1668f);
        private static readonly Rect HealthBarArt = Rect.MinMaxRect(649f, 528f, 1735f, 932f);
        private const float HealthFillLeft = 936f;
        private const float HealthFillRight = 1616f;
        private static readonly Rect TowerBarArt = Rect.MinMaxRect(551f, 522f, 1735f, 953f);
        private const float TowerFillLeft = 944f;
        private const float TowerFillRight = 1616f;

        private Rect bounds;
        private PlayerController player;
        private StardustSpawner stardust;
        private OasisMonsterSpawner monsterSpawner;
        private IslandSoftLight softLight;
        private Vector3 cameraVelocity;
        private RitualTower activeTower;
        private GameObject islandTwoGate;
        private int completedTowerCount;
        private bool changingScene;

        internal bool HasActiveTowerChallenge => activeTower && !changingScene;

        private void Awake()
        {
            viewCamera.orthographic = true;
            Bounds map = islandTerrain.bounds;
            bounds = Rect.MinMaxRect(map.min.x, map.min.y, map.max.x, map.max.y);

            CreateCrossingMarkers();
            SetUpOasisChallenges();
            player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            stardust = GetComponent<StardustSpawner>();
            monsterSpawner = GetComponent<OasisMonsterSpawner>();
            var trails = GetComponent<SolarTrails>();
            trails.Initialize(player);
            player.Initialize(trails, stardust, monsterSpawner, coastline, islandCrossings);
            player.Died += ReturnToMainMenu;
            viewCamera.transform.position = CameraPosition();
            if (viewCamera.TryGetComponent(out softLight))
                softLight.Initialize(player.transform, coastline);

            Physics2D.SyncTransforms();
            stardust.Initialize(bounds, viewCamera, player, startingStardust, pickupTemplates);
            if (monsterSpawner)
                monsterSpawner.Initialize(this, player, coastline);
        }

        private void OnDestroy()
        {
            if (player)
                player.Died -= ReturnToMainMenu;

            foreach (RitualTower tower in oasisTowers)
            {
                if (tower)
                    tower.FullBallHit -= StartTowerChallenge;
            }
        }

        private void SetUpOasisChallenges()
        {
            foreach (RitualTower tower in oasisTowers)
            {
                if (tower)
                    tower.FullBallHit += StartTowerChallenge;
            }

            if (oasisTowers.Length == 0)
                return;

            islandTwoGate = new GameObject("Island 1-2 Air Wall");
            islandTwoGate.transform.SetParent(coastline.transform, false);
            AddShoreWall("Island 1 Shore", islandOneShorePath);
            AddShoreWall("Island 2 Shore", islandTwoShorePath);
        }

        private void AddShoreWall(string name, int pathIndex)
        {
            Vector2[] shore = coastline.GetPath(pathIndex);
            var loop = new Vector2[shore.Length + 1];
            shore.CopyTo(loop, 0);
            loop[shore.Length] = shore[0];

            var wall = new GameObject(name);
            wall.layer = ArenaPhysics.BoundaryLayer;
            wall.transform.SetParent(islandTwoGate.transform, false);
            var edge = wall.AddComponent<EdgeCollider2D>();
            edge.offset = coastline.offset;
            edge.points = loop;
        }

        private void CreateCrossingMarkers()
        {
            Color marker = new Color(0.28f, 0.74f, 0.83f);
            Vector2 markerSize = new Vector2(0.12f, 0.62f);
            foreach (IslandCrossing crossing in islandCrossings)
            {
                ArenaSprites.Create("Crossing marker", transform, crossing.FirstBank, markerSize,
                    ArenaSprites.Square, marker, -3);
                ArenaSprites.Create("Crossing marker", transform, crossing.SecondBank, markerSize,
                    ArenaSprites.Square, marker, -3);
            }
        }

        private void Update()
        {
            if (changingScene)
                return;

            if (Input.GetKeyDown(KeyCode.H))
                player.TakeDamage(damageTestAmount);
            if (changingScene)
                return;

            if (Input.GetKeyDown(KeyCode.R))
            {
                ReplaceScene(gameObject.scene.name);
                return;
            }

            if (Input.GetKeyDown(KeyCode.F))
                player.FillBall();
            if (Input.GetKeyDown(KeyCode.T))
                AddTowerChallengeEnergy(TowerTestEnergy);
        }

        private void StartTowerChallenge(RitualTower tower)
        {
            if (changingScene || activeTower || tower.IsComplete)
                return;

            activeTower = tower;
        }

        internal void AddTowerChallengeEnergy(int amount)
        {
            if (changingScene || !activeTower)
                return;

            activeTower.AddEnergy(amount);
            if (!activeTower.IsComplete)
                return;

            activeTower = null;
            completedTowerCount++;
            if (islandTwoGate && completedTowerCount == oasisTowers.Length)
                islandTwoGate.SetActive(false);
        }

        private void ReturnToMainMenu()
        {
            ReplaceScene(MainMenuScene);
        }

        private void ReplaceScene(string sceneName)
        {
            if (changingScene)
                return;

            changingScene = true;
            player.enabled = false;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        private Vector3 CameraPosition()
        {
            Vector2 point = player.transform.position;
            float halfHeight = viewCamera.orthographicSize;
            float halfWidth = halfHeight * viewCamera.aspect;
            point.x = halfWidth * 2f >= bounds.width ? bounds.center.x : Mathf.Clamp(point.x, bounds.xMin + halfWidth, bounds.xMax - halfWidth);
            point.y = halfHeight * 2f >= bounds.height ? bounds.center.y : Mathf.Clamp(point.y, bounds.yMin + halfHeight, bounds.yMax - halfHeight);
            return new Vector3(point.x, point.y, -10f);
        }

        private void LateUpdate()
        {
            viewCamera.transform.position = Vector3.SmoothDamp(viewCamera.transform.position, CameraPosition(), ref cameraVelocity, 0.16f);
        }

        private void OnGUI()
        {
            if (!player)
                return;

            float progressBarWidth = DrawProgressBars();

            string charge = player.Charge >= 1f ? "FULL" : $"{Mathf.FloorToInt(player.Charge * 100f)}%";
            string ball = player.HasBall ? $"{charge}  |  Mass {player.BallMass:0.0}" : "Reforms as you move";
            float debugWidth = Mathf.Clamp(Screen.width - progressBarWidth - HudMargin * 3f, 0f, 500f);
            float debugHeight = softLight ? 302f : 262f;
            GUILayout.BeginArea(new Rect(Screen.width - debugWidth - HudMargin, HudMargin, debugWidth, debugHeight), GUI.skin.box);
            GUILayout.Label("SUNBEARER DEMO  /  OASIS");
            if (softLight)
            {
                GUILayout.Label("Soft light  0 Auto / 1-3 Island presets / 4 Boost / L On-Off");
                GUILayout.Label(softLight.PreviewStatus);
            }
            GUILayout.Label("WASD Move / Steer   /   F Fill ball   /   R Restart");
            GUILayout.Label("Hold Space or LMB for auto push   /   Release to shoot");
            GUILayout.Label("Cyan crossings: push a full ball toward the other bank.");
            GUILayout.Label($"Power-ups {stardust.PowerUpCount}   /   H Take {damageTestAmount:0.#} damage");
            GUILayout.Label($"{(player.IsPushing ? "Push" : "Free")}  |  Speed {player.CurrentSpeed:0.00}  |  HP {player.Health:0}/{player.MaxHealth:0}  |  Ball {ball}");
            GUILayout.Label($"Drops {stardust.Count + stardust.PowerUpCount}/{stardust.Capacity}  |  Last launch {player.LastLaunchSpeed:0.0}  |  Reflections {player.LastReflections}");
            GUILayout.Label($"Pickup {stardust.LastPickup}  |  Black hole {player.BlackHoleRemaining:0.0}s  |  Wings {player.SolarWingsRemaining:0.0}s");
            GUILayout.Label($"Stored stardust {player.StoredStardust}/{PlayerController.StardustReserveCapacity}  |  Damage immunity {player.InvulnerabilityRemaining:0.00}s");
            string gateState = islandTwoGate && islandTwoGate.activeSelf ? "Locked" : "Open";
            GUILayout.Label($"Oasis towers {completedTowerCount}/{oasisTowers.Length}  |  Island 2 {gateState}");
            GUILayout.Label(activeTower
                ? $"{activeTower.name} {activeTower.Energy}/{activeTower.EnergyRequired}  |  T Test charge +{TowerTestEnergy}"
                : "Hit an oasis tower with a full sun to start.");
            if (monsterSpawner)
                GUILayout.Label($"Monsters {monsterSpawner.AliveCount}  |  Spawn every {monsterSpawner.CurrentInterval:0.#}s");
            GUILayout.EndArea();
        }

        private float DrawProgressBars()
        {
            bool showHealth = healthBarFull && healthBarEmpty;
            bool showTower = activeTower && towerBarFull && towerBarEmpty;
            if (!showHealth && !showTower)
                return 0f;

            float width = Mathf.Min(Screen.width * 0.30f, Screen.height / 3f);
            if (Event.current.type != EventType.Repaint)
                return width;

            float y = HudMargin;
            if (showHealth)
            {
                float splitX = Mathf.Lerp(HealthFillLeft, HealthFillRight, player.Health / player.MaxHealth);
                DrawProgressBar(healthBarFull, healthBarEmpty, HealthBarArt, y, width, splitX);
                y += width * HealthBarArt.height / HealthBarArt.width + 6f;
            }

            if (showTower)
            {
                float progress = activeTower.Energy / (float)activeTower.EnergyRequired;
                float splitX = Mathf.Lerp(TowerFillLeft, TowerFillRight, progress);
                DrawProgressBar(towerBarFull, towerBarEmpty, TowerBarArt, y, width, splitX);

                Texture2D icon = activeTower.Icon;
                if (icon)
                    DrawProgressBarSlice(icon, TowerBarArt, TowerBarArt.xMin, TowerBarArt.xMax, y, width / TowerBarArt.width);
            }

            return width;
        }

        private static void DrawProgressBar(Texture2D full, Texture2D empty, Rect art, float y, float width, float splitX)
        {
            float scale = width / art.width;
            DrawProgressBarSlice(full, art, art.xMin, splitX, y, scale);
            DrawProgressBarSlice(empty, art, splitX, art.xMax, y, scale);
        }

        private static void DrawProgressBarSlice(Texture2D texture, Rect art, float startX, float endX, float y, float scale)
        {
            float sliceWidth = endX - startX;
            Rect position = new Rect(HudMargin + (startX - art.xMin) * scale, y,
                sliceWidth * scale, art.height * scale);
            Rect uv = new Rect(startX / ProgressArtSourceSize.x, 1f - art.yMax / ProgressArtSourceSize.y,
                sliceWidth / ProgressArtSourceSize.x, art.height / ProgressArtSourceSize.y);
            GUI.DrawTextureWithTexCoords(position, texture, uv);
        }
    }
}
