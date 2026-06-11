using UnityEngine;

namespace BusJam
{
    /// <summary>Builds low-poly visuals. Buses run lengthwise along +Z and point
    /// their arrow toward -Z (down the screen, toward the parking row).</summary>
    public static class LowPolyBuilder
    {
        public const float BusHeight = 0.6f;
        public const float BusWidth = 0.85f;
        public const float WheelRadius = 0.16f;

        static GameObject Prim(PrimitiveType type, Transform parent, Material mat, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!keepCollider)
            {
                var c = go.GetComponent<Collider>();
                if (c != null) Object.Destroy(c);
            }
            go.transform.SetParent(parent, false);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>True body length = the vehicle's L grid cells (Car 1 / Bus 2 / Limo 3) at ~0.9 fill, so a
        /// vehicle spans its WHOLE footprint and the board reads as a packed jam.</summary>
        public static float VehicleLength(VehicleType type, float cellSize) => cellSize * Vehicles.CellLength(type) * 0.9f;

        // Car = short & tall, Bus = standard, Limo = long & low. Seat count = capacity.
        public static Renderer[] BuildVehicle(Transform root, VehicleType type, int capacity, float cellSize,
            Material body, Material glass, Material wheel, Material light, Material seatEmpty, Material arrowMat)
        {
            float len = VehicleLength(type, cellSize);                                   // spans the L cells (along Z)
            float w   = cellSize * 0.52f;                                                // proportional width (along X) -- not stretched
            float h   = cellSize * (type == VehicleType.Car ? 0.46f : type == VehicleType.Limo ? 0.32f : 0.42f);
            float wr  = cellSize * 0.11f;    // wheel radius
            float bodyY = wr + h * 0.5f;
            float top = wr + h;

            var bodyGo = Prim(PrimitiveType.Cube, root, body, keepCollider: true);
            bodyGo.name = "Body";
            bodyGo.transform.localScale = new Vector3(w, h, len);
            bodyGo.transform.localPosition = new Vector3(0, bodyY, 0);

            // Windshield at the front (-Z)
            var wind = Prim(PrimitiveType.Cube, root, glass);
            wind.transform.localScale = new Vector3(w * 0.86f, h * 0.5f, len * 0.14f);
            wind.transform.localPosition = new Vector3(0, bodyY + h * 0.12f, -len * 0.5f - 0.005f);

            // Side window strips
            for (int side = -1; side <= 1; side += 2)
            {
                var sw = Prim(PrimitiveType.Cube, root, glass);
                sw.transform.localScale = new Vector3(0.03f, h * 0.34f, len * 0.7f);
                sw.transform.localPosition = new Vector3(side * (w * 0.5f + 0.005f), bodyY + h * 0.15f, 0);
            }

            // Seat dots on the roof (light up as people board)
            var seats = new Renderer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                float z = -len * 0.5f + (i + 1) * (len / (capacity + 1));
                var s = Prim(PrimitiveType.Cube, root, seatEmpty);
                s.name = "Seat" + i;
                s.transform.localScale = new Vector3(w * 0.5f, 0.05f, len / (capacity + 2));
                s.transform.localPosition = new Vector3(0, top + 0.01f, z);
                seats[i] = s.GetComponent<Renderer>();
            }

            // Arrow on top, pointing -Z. Static (no idle pulse) so the grid is dead-still.
            var arrowPivot = new GameObject("Arrow");
            arrowPivot.transform.SetParent(root, false);
            arrowPivot.transform.localPosition = new Vector3(0, top + 0.12f, -len * 0.5f + 0.06f);
            var shaft = Prim(PrimitiveType.Cube, arrowPivot.transform, arrowMat);
            shaft.transform.localScale = new Vector3(cellSize * 0.06f, 0.06f, cellSize * 0.22f);
            shaft.transform.localPosition = new Vector3(0, 0, cellSize * 0.1f);
            var head = Prim(PrimitiveType.Cube, arrowPivot.transform, arrowMat);
            head.transform.localScale = new Vector3(cellSize * 0.18f, 0.06f, cellSize * 0.18f);
            head.transform.localPosition = new Vector3(0, 0, -cellSize * 0.03f);
            head.transform.localRotation = Quaternion.Euler(0, 45, 0);

            // Wheels (along ±X, front/back)
            float wx = w * 0.52f, wz = len * 0.34f;
            Wheel(root, wheel, new Vector3(wx, wr, wz), wr);
            Wheel(root, wheel, new Vector3(wx, wr, -wz), wr);
            Wheel(root, wheel, new Vector3(-wx, wr, wz), wr);
            Wheel(root, wheel, new Vector3(-wx, wr, -wz), wr);

            // Headlights at the front
            Light2(root, light, new Vector3(wx * 0.5f, wr + 0.05f, -len * 0.5f - 0.01f));
            Light2(root, light, new Vector3(-wx * 0.5f, wr + 0.05f, -len * 0.5f - 0.01f));
            return seats;
        }

        static void Wheel(Transform parent, Material mat, Vector3 pos, float r)
        {
            var wgo = Prim(PrimitiveType.Cylinder, parent, mat);
            wgo.transform.localRotation = Quaternion.Euler(0, 0, 90); // axis -> X
            wgo.transform.localScale = new Vector3(r * 2f, 0.04f, r * 2f);
            wgo.transform.localPosition = pos;
        }

        static void Light2(Transform parent, Material mat, Vector3 pos)
        {
            var l = Prim(PrimitiveType.Sphere, parent, mat);
            l.transform.localScale = Vector3.one * 0.12f;
            l.transform.localPosition = pos;
        }

        public static Renderer BuildPerson(Transform root, Material colorMat, Material skin,
            bool golden, bool mystery, Material mysteryMat, Material goldMat, out GameObject cover)
        {
            cover = null;
            Material shirt = mystery ? mysteryMat : colorMat;

            // Visual child so gameplay can move the root while this gently bobs.
            var vis = new GameObject("Vis");
            vis.transform.SetParent(root, false);
            var bob = vis.AddComponent<IdleBob>();
            bob.amp = 0.05f; bob.speed = 2.3f; bob.phase = Random.value * 6.28f;

            var torso = Prim(PrimitiveType.Capsule, vis.transform, shirt);
            torso.transform.localScale = new Vector3(0.44f, 0.42f, 0.44f);
            torso.transform.localPosition = new Vector3(0, 0.42f, 0);
            Renderer bodyR = torso.GetComponent<Renderer>();

            var head = Prim(PrimitiveType.Sphere, vis.transform, skin);
            head.transform.localScale = Vector3.one * 0.38f;        // big cute head
            head.transform.localPosition = new Vector3(0, 0.92f, 0);

            if (mystery)
            {
                var q = Prim(PrimitiveType.Cube, vis.transform, mysteryMat);
                q.transform.localScale = Vector3.one * 0.17f;
                q.transform.localPosition = new Vector3(0, 1.3f, 0);
                q.transform.localRotation = Quaternion.Euler(0, 45, 0);
                cover = q;
            }
            if (golden)
            {
                var crown = Prim(PrimitiveType.Cube, vis.transform, goldMat);
                crown.transform.localScale = new Vector3(0.22f, 0.1f, 0.22f);
                crown.transform.localPosition = new Vector3(0, 1.18f, 0);
                crown.transform.localRotation = Quaternion.Euler(0, 45, 0);
                var spin = crown.AddComponent<IdleBob>();
                spin.scalePulse = true; spin.scaleAmp = 0.2f; spin.speed = 5f; spin.amp = 0f;
            }
            return bodyR;
        }

        // ---- Theme props ----------------------------------------------------
        public static void BuildProp(Transform parent, PropKind kind, Vector3 pos,
            Material main, Material alt, Material foliage, Material trunk, Material window, float scale)
        {
            switch (kind)
            {
                case PropKind.Building:  Building(parent, pos, scale, main, window); break;
                case PropKind.Cactus:    Cactus(parent, pos, scale, foliage); break;
                case PropKind.Pine:      Pine(parent, pos, scale, foliage, trunk); break;
                case PropKind.Palm:      Palm(parent, pos, scale, foliage, trunk); break;
                case PropKind.RoundTree: RoundTree(parent, pos, scale, foliage, trunk); break;
                case PropKind.Bush:      Bush(parent, pos, scale, foliage); break;
                case PropKind.House:     House(parent, pos, scale, main, alt, window); break;
            }
        }

        // Cute rounded tree: short trunk + a blob canopy of 3 overlapping spheres.
        static void RoundTree(Transform parent, Vector3 pos, float scale, Material leaves, Material trunk)
        {
            var t = Prim(PrimitiveType.Cylinder, parent, trunk);
            t.transform.position = pos + new Vector3(0, 0.5f * scale, 0);
            t.transform.localScale = new Vector3(0.2f * scale, 0.5f * scale, 0.2f * scale);
            var c1 = Prim(PrimitiveType.Sphere, parent, leaves);
            c1.transform.position = pos + new Vector3(0, 1.3f * scale, 0);
            c1.transform.localScale = Vector3.one * 1.35f * scale;
            var c2 = Prim(PrimitiveType.Sphere, parent, leaves);
            c2.transform.position = pos + new Vector3(0.42f * scale, 1.5f * scale, 0.12f * scale);
            c2.transform.localScale = Vector3.one * 0.95f * scale;
            var c3 = Prim(PrimitiveType.Sphere, parent, leaves);
            c3.transform.position = pos + new Vector3(-0.38f * scale, 1.46f * scale, -0.12f * scale);
            c3.transform.localScale = Vector3.one * 1.0f * scale;
        }

        // Low rounded bush: two squashed spheres.
        static void Bush(Transform parent, Vector3 pos, float scale, Material leaves)
        {
            var a = Prim(PrimitiveType.Sphere, parent, leaves);
            a.transform.position = pos + new Vector3(0, 0.36f * scale, 0);
            a.transform.localScale = new Vector3(1.0f * scale, 0.7f * scale, 1.0f * scale);
            var b = Prim(PrimitiveType.Sphere, parent, leaves);
            b.transform.position = pos + new Vector3(0.46f * scale, 0.30f * scale, 0.06f * scale);
            b.transform.localScale = new Vector3(0.72f * scale, 0.56f * scale, 0.72f * scale);
        }

        // Cute cottage: body box + wider roof block + door + two windows (front = -Z, toward camera).
        static void House(Transform parent, Vector3 pos, float scale, Material body, Material roof, Material window)
        {
            float w = 2.3f * scale, h = 1.7f * scale, d = 2.3f * scale;
            var b = Prim(PrimitiveType.Cube, parent, body);
            b.transform.position = pos + new Vector3(0, h * 0.5f, 0);
            b.transform.localScale = new Vector3(w, h, d);
            var rf = Prim(PrimitiveType.Cube, parent, roof);
            rf.transform.position = pos + new Vector3(0, h + 0.32f * scale, 0);
            rf.transform.localScale = new Vector3(w * 1.18f, 0.64f * scale, d * 1.18f);
            var door = Prim(PrimitiveType.Cube, parent, roof);
            door.transform.position = pos + new Vector3(0, 0.55f * scale, -d * 0.5f - 0.03f);
            door.transform.localScale = new Vector3(0.55f * scale, 1.0f * scale, 0.08f);
            for (int s = -1; s <= 1; s += 2)
            {
                var win = Prim(PrimitiveType.Cube, parent, window);
                win.transform.position = pos + new Vector3(s * 0.62f * scale, h * 0.62f, -d * 0.5f - 0.03f);
                win.transform.localScale = new Vector3(0.5f * scale, 0.5f * scale, 0.08f);
            }
        }

        /// <summary>A small clump of grass blades (cheap) for dressing the lawn.</summary>
        public static void GrassTuft(Transform parent, Vector3 pos, float scale, Material grass)
        {
            for (int i = 0; i < 3; i++)
            {
                var blade = Prim(PrimitiveType.Cube, parent, grass);
                float ox = (i - 1) * 0.10f * scale;
                blade.transform.position = pos + new Vector3(ox, 0.16f * scale, 0);
                blade.transform.localScale = new Vector3(0.06f * scale, 0.32f * scale, 0.06f * scale);
                blade.transform.localRotation = Quaternion.Euler(0, 0, (i - 1) * 12f);
            }
        }

        static void Building(Transform parent, Vector3 pos, float scale, Material body, Material window)
        {
            float hgt = 3.5f * scale;
            var b = Prim(PrimitiveType.Cube, parent, body);
            b.transform.position = pos + new Vector3(0, hgt / 2f, 0);
            b.transform.localScale = new Vector3(2.6f * scale, hgt, 2.6f * scale);
            for (int i = 0; i < 3; i++)
            {
                var win = Prim(PrimitiveType.Cube, parent, window);
                win.transform.position = pos + new Vector3(0, hgt * (0.25f + i * 0.25f), -1.3f * scale - 0.02f);
                win.transform.localScale = new Vector3(1.8f * scale, hgt * 0.12f, 0.05f);
            }
        }

        static void Cactus(Transform parent, Vector3 pos, float scale, Material green)
        {
            var b = Prim(PrimitiveType.Capsule, parent, green);
            b.transform.position = pos + new Vector3(0, 0.9f * scale, 0);
            b.transform.localScale = new Vector3(0.4f * scale, 0.9f * scale, 0.4f * scale);
            var arm = Prim(PrimitiveType.Capsule, parent, green);
            arm.transform.position = pos + new Vector3(0.35f * scale, 1.0f * scale, 0);
            arm.transform.localScale = new Vector3(0.18f * scale, 0.4f * scale, 0.18f * scale);
        }

        static void Pine(Transform parent, Vector3 pos, float scale, Material leaves, Material trunk)
        {
            var t = Prim(PrimitiveType.Cylinder, parent, trunk);
            t.transform.position = pos + new Vector3(0, 0.4f * scale, 0);
            t.transform.localScale = new Vector3(0.16f * scale, 0.4f * scale, 0.16f * scale);
            for (int i = 0; i < 3; i++)
            {
                var l = Prim(PrimitiveType.Sphere, parent, leaves);
                l.transform.position = pos + new Vector3(0, (0.9f + i * 0.5f) * scale, 0);
                l.transform.localScale = Vector3.one * (1.1f - i * 0.25f) * scale;
            }
        }

        static void Palm(Transform parent, Vector3 pos, float scale, Material leaves, Material trunk)
        {
            var t = Prim(PrimitiveType.Cylinder, parent, trunk);
            t.transform.position = pos + new Vector3(0, 0.9f * scale, 0);
            t.transform.localScale = new Vector3(0.14f * scale, 0.9f * scale, 0.14f * scale);
            var crown = Prim(PrimitiveType.Sphere, parent, leaves);
            crown.transform.position = pos + new Vector3(0, 1.9f * scale, 0);
            crown.transform.localScale = new Vector3(1.5f * scale, 0.5f * scale, 1.5f * scale);
        }

        public static GameObject Slab(Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}
