using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sunbearer
{
    [RequireComponent(typeof(SolarTrails), typeof(StardustSpawner))]
    public sealed class Playground : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerController playerPrefab;

        private static readonly Rect Bounds = new Rect(-17f, -9f, 34f, 18f);
        private static readonly Color WallColor = new Color(0.45f, 0.48f, 0.55f);

        private PlayerController player;
        private StardustSpawner stardust;
        private PracticeTarget target;
        private Vector3 cameraVelocity;

        private void Awake()
        {
            viewCamera.orthographic = true;
            viewCamera.orthographicSize = 4.8f;
            viewCamera.backgroundColor = new Color(0.067f, 0.085f, 0.12f);

            BuildArena();
            player = Instantiate(playerPrefab, new Vector3(-13f, 0f, 0f), Quaternion.identity);
            stardust = GetComponent<StardustSpawner>();
            player.Initialize(GetComponent<SolarTrails>(), viewCamera, stardust);
            viewCamera.transform.position = CameraPosition();

            Physics2D.SyncTransforms();
            stardust.Initialize(Bounds, viewCamera, player);
        }

        private void BuildArena()
        {
            ArenaSprites.Create("Ground", transform, Bounds.center, Bounds.size, ArenaSprites.Square,
                new Color(0.19f, 0.24f, 0.27f), -8);
            AddBlock("Gap", new Rect(-0.9f, Bounds.yMin, 2.4f, Bounds.height), ArenaPhysics.GapLayer,
                new Color(0.085f, 0.11f, 0.16f), -5);

            AddBlock("West edge", new Rect(Bounds.xMin - 0.6f, Bounds.yMin, 0.6f, Bounds.height),
                ArenaPhysics.BoundaryLayer, WallColor, 0);
            AddBlock("East edge", new Rect(Bounds.xMax, Bounds.yMin, 0.6f, Bounds.height),
                ArenaPhysics.BoundaryLayer, WallColor, 0);
            AddBlock("North edge", new Rect(Bounds.xMin - 0.6f, Bounds.yMax, Bounds.width + 1.2f, 0.6f),
                ArenaPhysics.BoundaryLayer, WallColor, 0);
            AddBlock("South edge", new Rect(Bounds.xMin - 0.6f, Bounds.yMin - 0.6f, Bounds.width + 1.2f, 0.6f),
                ArenaPhysics.BoundaryLayer, WallColor, 0);

            AddBlock("West wall", new Rect(-8f, 3.4f, 4.2f, 0.6f), ArenaPhysics.WallLayer, WallColor, 0);
            AddBlock("East wall", new Rect(7.8f, -2.5f, 0.6f, 4.5f), ArenaPhysics.WallLayer, WallColor, 0);
            AddBlock("Reflection lane north", new Rect(-14f, -4.9f, 5f, 0.5f), ArenaPhysics.WallLayer, WallColor, 0);
            AddBlock("Reflection lane south", new Rect(-14f, -7.9f, 5f, 0.5f), ArenaPhysics.WallLayer, WallColor, 0);

            Color marker = new Color(0.28f, 0.74f, 0.83f);
            ArenaSprites.Create("Bridge launch marker", transform, new Vector2(-2.35f, 0f), new Vector2(0.2f, 1.3f),
                ArenaSprites.Square, marker, -3);
            ArenaSprites.Create("Bridge landing marker", transform, new Vector2(2.45f, 0f), new Vector2(0.2f, 1.3f),
                ArenaSprites.Square, marker, -3);

            var dummy = ArenaSprites.Create("Impact dummy", transform, new Vector2(-9.8f, 1.1f),
                Vector2.one, ArenaSprites.Disc, new Color(0.94f, 0.35f, 0.34f), 3);
            dummy.gameObject.layer = ArenaPhysics.TargetLayer;
            dummy.gameObject.AddComponent<CircleCollider2D>().radius = PracticeTarget.Radius;
            target = dummy.gameObject.AddComponent<PracticeTarget>();
        }

        private void AddBlock(string name, Rect bounds, int layer, Color color, int order)
        {
            var sprite = ArenaSprites.Create(name, transform, bounds.center, bounds.size, ArenaSprites.Square, color, order);
            sprite.gameObject.layer = layer;
            sprite.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F))
                player.FillBall();
            if (Input.GetKeyDown(KeyCode.R))
                SceneManager.LoadScene(gameObject.scene.name);
        }

        private Vector3 CameraPosition()
        {
            Vector2 point = player.transform.position;
            float halfHeight = viewCamera.orthographicSize;
            float halfWidth = halfHeight * viewCamera.aspect;
            point.x = halfWidth * 2f >= Bounds.width ? Bounds.center.x : Mathf.Clamp(point.x, Bounds.xMin + halfWidth, Bounds.xMax - halfWidth);
            point.y = halfHeight * 2f >= Bounds.height ? Bounds.center.y : Mathf.Clamp(point.y, Bounds.yMin + halfHeight, Bounds.yMax - halfHeight);
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

            string charge = player.Charge >= 1f ? "FULL" : $"{Mathf.FloorToInt(player.Charge * 100f)}%";
            string ball = player.HasBall ? $"{charge}  |  Mass {player.BallMass:0.0}" : "Reforms as you move";
            GUILayout.BeginArea(new Rect(12f, 12f, 450f, 164f), GUI.skin.box);
            GUILayout.Label("SUNBEARER  /  PLAYGROUND");
            GUILayout.Label("WASD Move   /   Hold Space Push   /   Release Space Shoot");
            GUILayout.Label("Mouse Steer   /   F Fill ball   /   R Restart");
            GUILayout.Label("Bridge from the blue mark. Bounce between the southwest walls.");
            GUILayout.Label($"{(player.IsPushing ? "Push" : "Free")}  |  Speed {player.CurrentSpeed:0.0}  |  Max HP {player.MaxHealth:0}  |  Ball {ball}");
            GUILayout.Label($"Stardust {stardust.Count}/{stardust.Capacity}  |  Last launch {player.LastLaunchSpeed:0.0}  |  Reflections {player.LastReflections}");
            GUILayout.Label($"Dummy HP {target.Health:0}  |  Last damage {target.LastDamage:0.0}  |  Knockback {target.LastKnockback:0.0}");
            GUILayout.EndArea();
        }
    }
}
