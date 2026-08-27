using UnityEngine;

namespace Ridebury
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

        /// <summary>True body length = the vehicle's L grid cells (Car 1 / Bus 2) at ~0.9 fill, so a
        /// vehicle spans its WHOLE footprint and the board reads as a packed jam.</summary>
        public static float VehicleLength(VehicleType type, float cellSize) => cellSize * Vehicles.CellLength(type) * 0.9f;

        static Mesh _arrowHead;
        /// <summary>Shared flat triangular arrowhead (unit: tip at -Z, base at +Z, width 1). Scale per use; ONE
        /// mesh is reused by every vehicle so there is no per-vehicle allocation or cross-level leak.</summary>
        public static Mesh ArrowHeadMesh()
        {
            if (_arrowHead == null)
            {
                _arrowHead = new Mesh { name = "ArrowHead" };
                _arrowHead.vertices = new[]
                {
                    new Vector3(0f, 0f, -0.5f),   // tip toward -Z (the exit direction)
                    new Vector3(-0.5f, 0f, 0.5f), // back-left
                    new Vector3(0.5f, 0f, 0.5f),  // back-right
                };
                _arrowHead.triangles = new[] { 0, 1, 2 }; // single up-facing tri (winding -> +Y normal so the sun lights it; the top-down camera always sees this face)
                _arrowHead.RecalculateNormals();
                _arrowHead.RecalculateBounds();
            }
            return _arrowHead;
        }

        // Car = short & tall, Bus = standard. Roof heads (boarded passengers) are built separately by
        // RideburyGame.BuildRoofHeads, unified across both render paths.
        public static void BuildVehicle(Transform root, VehicleType type, float cellSize,
            Material body, Material glass, Material wheel, Material light, Material arrowMat)
        {
            float len = VehicleLength(type, cellSize);                                   // spans the L cells (along Z)
            float w   = cellSize * 0.52f;                                                // proportional width (along X) -- not stretched
            float h   = cellSize * (type == VehicleType.Bus ? 0.42f : 0.46f); // Bus is lower/longer; Car & Minivan (1-cell) sit taller
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

            // Clear arrow on top, pointing -Z (the exit dir): a SOLID TRIANGULAR head + a shaft. Reads as a real
            // arrow from the top-down camera (the old 45° "diamond" head was ambiguous). Static, no idle pulse.
            var arrowPivot = new GameObject("Arrow");
            arrowPivot.transform.SetParent(root, false);
            arrowPivot.transform.localPosition = new Vector3(0, top + 0.10f, -len * 0.5f + 0.06f);
            var head = new GameObject("ArrowHead");
            head.transform.SetParent(arrowPivot.transform, false);
            head.transform.localPosition = new Vector3(0, 0.03f, -cellSize * 0.02f);  // tip toward -Z
            head.transform.localScale = new Vector3(cellSize * 0.44f, 1f, cellSize * 0.24f);
            head.AddComponent<MeshFilter>().sharedMesh = ArrowHeadMesh();
            head.AddComponent<MeshRenderer>().sharedMaterial = arrowMat;
            var shaft = Prim(PrimitiveType.Cube, arrowPivot.transform, arrowMat);
            shaft.transform.localScale = new Vector3(cellSize * 0.15f, 0.05f, cellSize * 0.20f);
            shaft.transform.localPosition = new Vector3(0, 0.03f, cellSize * 0.20f);

            // Wheels (along ±X, front/back)
            float wx = w * 0.52f, wz = len * 0.34f;
            Wheel(root, wheel, new Vector3(wx, wr, wz), wr);
            Wheel(root, wheel, new Vector3(wx, wr, -wz), wr);
            Wheel(root, wheel, new Vector3(-wx, wr, wz), wr);
            Wheel(root, wheel, new Vector3(-wx, wr, -wz), wr);

            // Headlights at the front
            Light2(root, light, new Vector3(wx * 0.5f, wr + 0.05f, -len * 0.5f - 0.01f));
            Light2(root, light, new Vector3(-wx * 0.5f, wr + 0.05f, -len * 0.5f - 0.01f));
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
                cover = BuildMysteryMark(vis.transform, mysteryMat, 1.2f, 0.42f); // "?" on top of the head, like a hat
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

        // Mystery marker: a camera-facing "?" badge placed ON the passenger's chest (not floating above the head), so the
        // gray body clearly reads as "hidden colour". Same proven world-space setup as the special-vehicle badge; the badge
        // is pushed toward the camera (BillboardUp aims local +Z at the cam) so it draws IN FRONT of the body, not inside it.
        // Returned as ONE parent so Passenger/LineUnit.Reveal can Destroy() it (mysteryCover). chestY = chest height; badge = world size.
        public static GameObject BuildMysteryMark(Transform parent, Material mysteryMat, float chestY, float badge)
        {
            var mark = new GameObject("Mystery");
            mark.transform.SetParent(parent, false);
            mark.transform.localPosition = new Vector3(0, chestY, 0);

            var qGo = new GameObject("QMark", typeof(RectTransform), typeof(Canvas));
            qGo.transform.SetParent(mark.transform, false);
            qGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)qGo.transform).sizeDelta = new Vector2(100, 100);
            qGo.transform.localScale = new Vector3(-1f, 1f, 1f) * (badge / 100f);
            qGo.AddComponent<BillboardUp>();

            // Push the badge toward the camera so it draws on the FRONT of the body (BillboardUp aims local +Z at the cam).
            var c = new GameObject("C", typeof(RectTransform));
            c.transform.SetParent(qGo.transform, false);
            var crt = (RectTransform)c.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(100, 100);
            crt.localPosition = new Vector3(0, 0, 60f);

            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(c.transform, false);
            var t = tGo.AddComponent<UnityEngine.UI.Text>();
            t.font = GameFont.UGUI;
            t.text = "?";
            t.fontSize = 82;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var outline = tGo.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3, -3);
            tGo.AddComponent<FirstFrameTextRefresh>(); // so passengers ALREADY in line at level start show the "?" (cold-atlas fix)
            return mark;
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

        // ---- Helicopter (joker) -------------------------------------------------
        // A mini low-poly rescue chopper built from primitives, matching the toy look of the vehicles.
        // Nose points +Z so LookRotation(travelDir) aims it forward. The main + tail rotors auto-spin
        // (Spinner). The lift cable + hook are NOT built here — RideburyGame creates+animates them in world
        // space so they hang straight down regardless of how the body banks. Returns the body root.
        public static GameObject BuildHelicopter(Transform parent, Material body, Material glass, Material rotor, Material accent)
        {
            var root = new GameObject("Helicopter");
            if (parent != null) root.transform.SetParent(parent, false);

            // Fuselage — rounded capsule lying along Z.
            var fus = Prim(PrimitiveType.Capsule, root.transform, body);
            fus.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            fus.transform.localScale = new Vector3(0.66f, 0.9f, 0.66f);
            fus.transform.localPosition = new Vector3(0, 0, 0.05f);

            // Belly pod (slightly boxy underside the cable hangs from).
            var belly = Prim(PrimitiveType.Cube, root.transform, body);
            belly.transform.localScale = new Vector3(0.52f, 0.32f, 1.0f);
            belly.transform.localPosition = new Vector3(0, -0.22f, 0.05f);

            // Cockpit glass bubble at the nose (+Z).
            var glassGo = Prim(PrimitiveType.Sphere, root.transform, glass);
            glassGo.transform.localScale = new Vector3(0.6f, 0.55f, 0.66f);
            glassGo.transform.localPosition = new Vector3(0, 0.06f, 0.72f);

            // Tail boom (thin, back -Z) + vertical fin.
            var boom = Prim(PrimitiveType.Cube, root.transform, body);
            boom.transform.localScale = new Vector3(0.14f, 0.16f, 1.05f);
            boom.transform.localPosition = new Vector3(0, 0.14f, -0.95f);
            var fin = Prim(PrimitiveType.Cube, root.transform, accent);
            fin.transform.localScale = new Vector3(0.07f, 0.42f, 0.3f);
            fin.transform.localPosition = new Vector3(0, 0.34f, -1.45f);

            // Main rotor — mast + hub + a spinning 2-blade cross on top.
            var mast = Prim(PrimitiveType.Cylinder, root.transform, rotor);
            mast.transform.localScale = new Vector3(0.08f, 0.13f, 0.08f);
            mast.transform.localPosition = new Vector3(0, 0.52f, 0.05f);
            var hub = Prim(PrimitiveType.Cylinder, root.transform, accent);
            hub.transform.localScale = new Vector3(0.18f, 0.04f, 0.18f);
            hub.transform.localPosition = new Vector3(0, 0.66f, 0.05f);
            var mainRotor = new GameObject("MainRotor");
            mainRotor.transform.SetParent(root.transform, false);
            mainRotor.transform.localPosition = new Vector3(0, 0.68f, 0.05f);
            for (int i = 0; i < 2; i++)
            {
                var blade = Prim(PrimitiveType.Cube, mainRotor.transform, rotor);
                blade.transform.localScale = new Vector3(2.5f, 0.035f, 0.16f);
                blade.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);
            }
            mainRotor.AddComponent<Spinner>().Set(Vector3.up, 1150f);

            // Tail rotor — a small spinning cross at the boom end (spins about X).
            var tailRotor = new GameObject("TailRotor");
            tailRotor.transform.SetParent(root.transform, false);
            tailRotor.transform.localPosition = new Vector3(0.12f, 0.34f, -1.5f);
            for (int i = 0; i < 2; i++)
            {
                var blade = Prim(PrimitiveType.Cube, tailRotor.transform, rotor);
                blade.transform.localScale = new Vector3(0.05f, 0.62f, 0.07f);
                blade.transform.localRotation = Quaternion.Euler(0, 0, i * 90f);
            }
            tailRotor.AddComponent<Spinner>().Set(Vector3.right, 1700f);

            // Landing skids (two rails + struts).
            for (int s = -1; s <= 1; s += 2)
            {
                var skid = Prim(PrimitiveType.Cube, root.transform, rotor);
                skid.transform.localScale = new Vector3(0.05f, 0.05f, 1.2f);
                skid.transform.localPosition = new Vector3(s * 0.34f, -0.46f, 0.05f);
                var strutF = Prim(PrimitiveType.Cube, root.transform, rotor);
                strutF.transform.localScale = new Vector3(0.04f, 0.26f, 0.04f);
                strutF.transform.localPosition = new Vector3(s * 0.3f, -0.34f, 0.42f);
                var strutB = Prim(PrimitiveType.Cube, root.transform, rotor);
                strutB.transform.localScale = new Vector3(0.04f, 0.26f, 0.04f);
                strutB.transform.localPosition = new Vector3(s * 0.3f, -0.34f, -0.32f);
            }

            return root;
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
