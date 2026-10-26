// Models and textures for PotatoFighter's weapons, loaded from the assets folder beside the DLL (no Unity asset bundle),
// like PotatoFarmer's pieces. tools/convert.py, relayout.py and orient.py make them from a Meshy .glb:
// <name>.mesh, <name>_d.png (colour), <name>_n.png (normal), <name>_m.png (metal), <name>_e.png (glow).
using System.IO;
using Jotunn.Managers;
using UnityEngine;

namespace PotatoFighter
{
    internal static class Assets
    {
        internal static string Dir;

        internal static Mesh LoadMesh(string name)
        {
            string path = Path.Combine(Dir, name + ".mesh");
            if (!File.Exists(path))
            {
                Plugin.Log.LogError("Missing " + path);
                return null;
            }
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(reader.ReadChars(4)) != "PTM1")
                {
                    Plugin.Log.LogError(path + " isn't a model file from the PotatoFighter tools.");
                    return null;
                }
                int vertices = reader.ReadInt32(), indices = reader.ReadInt32();
                var positions = new Vector3[vertices];
                var normals = new Vector3[vertices];
                var uvs = new Vector2[vertices];
                var triangles = new int[indices];
                for (int i = 0; i < vertices; i++) positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < vertices; i++) normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < vertices; i++) uvs[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                for (int i = 0; i < indices; i++) triangles[i] = reader.ReadInt32();
                var mesh = new Mesh { name = "potato_" + name };
                if (vertices > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = positions;
                mesh.normals = normals;
                mesh.uv = uvs;
                mesh.triangles = triangles;
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // Sharp, unblended pixels like the game's own weapons. Null on a dedicated server (no graphics) or if missing.
        internal static Texture2D LoadTexture(string file, bool linear)
        {
            if (GUIManager.IsHeadless()) return null;
            string path = Path.Combine(Dir, file);
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("Missing " + path);
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = Path.GetFileNameWithoutExtension(file), wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true))
            {
                Plugin.Log.LogWarning("Couldn't read " + path);
                return null;
            }
            texture.filterMode = FilterMode.Point;
            return texture;
        }
    }
}
