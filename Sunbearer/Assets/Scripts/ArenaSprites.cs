using UnityEngine;

namespace Sunbearer
{
    internal static class ArenaSprites
    {
        private static Sprite square;
        private static Sprite disc;

        public static Sprite Square
        {
            get
            {
                if (!square)
                    square = MakeSprite(Texture2D.whiteTexture);
                return square;
            }
        }

        public static Sprite Disc
        {
            get
            {
                if (!disc)
                {
                    const int size = 64;
                    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    var pixels = new Color[size * size];
                    var center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float alpha = Mathf.Clamp01(size / 2f - Vector2.Distance(center, new Vector2(x, y)));
                            pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                        }
                    }

                    texture.SetPixels(pixels);
                    texture.Apply(false, true);
                    disc = MakeSprite(texture);
                }

                return disc;
            }
        }

        public static SpriteRenderer Create(string name, Transform parent, Vector2 position, Vector2 size,
            Sprite sprite, Color color, int order)
        {
            var shape = new GameObject(name);
            shape.transform.SetParent(parent, false);
            shape.transform.position = position;
            shape.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = shape.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static Sprite MakeSprite(Texture2D texture)
        {
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), texture.width, 0, SpriteMeshType.FullRect);
        }
    }
}
