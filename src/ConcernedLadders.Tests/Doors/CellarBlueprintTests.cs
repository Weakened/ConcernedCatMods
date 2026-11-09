using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TheConcernedCat.ConcernedLadders.Domain.Doors;
using TheConcernedCat.Ladders;

namespace TheConcernedCat.ConcernedLadders.Tests.Doors;

/// <summary>CL-005 #486 slice 2, the cellar door's piece adapter: registered only with its own off-by-default switch;
/// the donor checked, the clone read back against the cellar door's own cost, stripped and verified through the one
/// helper the trapdoors use; the frame meshes are exactly the model's convex prisms, closed and facing out; the slope
/// transform, hinge pivots and leaf boxes reproduce the model's leaves; the hinge marks stay clear of the cheeks at every
/// angle; and nothing is added to the network.</summary>
public sealed class CellarBlueprintTests
{
    private const float Epsilon = 1e-4f;

    private const string Adapter = "Pieces/CellarDoorPieceRegistration.cs";

    private const string Kit = "Pieces/DoorPieceKit.cs";

    private const string TrapdoorAdapter = "Pieces/TrapdoorPieceRegistration.cs";

    // ---- The switch ----------------------------------------------------------------------------------------------

    [Fact]
    public void ThePluginRegistersCellarDoorsOnlyWhenTheirOwnSettingIsOn()
    {
        string plugin = Code(Source("Plugin.cs"));
        Assert.Matches(new Regex(@"Config\s*\.\s*Bind\s*\(\s*CellarDoorSettings\s*\.\s*Section\s*,\s*CellarDoorSettings\s*\.\s*Key\s*,\s*CellarDoorSettings\s*\.\s*EnabledByDefault\s*,"), plugin);
        Assert.Single(Regex.Matches(plugin, @"new\s+CellarDoorPieceRegistration\s*\("));
        Assert.Matches(new Regex(@"if\s*\(\s*_cellarDoorsEnabled\s*\.\s*Value\s*\)\s*\{\s*_cellarDoors\s*=\s*new\s+CellarDoorPieceRegistration\s*\([^;]*\)\s*;\s*try\s*\{\s*_cellarDoors\s*\.\s*Install\s*\(\s*\)\s*;\s*\}\s*catch"), plugin);
        Assert.Single(Regex.Matches(plugin, @"_cellarDoors\s*\.\s*Install\s*\("));
        // Torn down in OnDestroy, after the trapdoors; the install-failure path disposes its own instance.
        Assert.Single(Regex.Matches(plugin, @"_cellarDoors\s*\?\s*\.\s*Dispose\s*\(\s*\)\s*;"));
        Assert.Matches(new Regex(@"private\s+void\s+OnDestroy\s*\(\s*\)[^}]*?(\{[^{}]*\}[^{}]*?)*_trapdoors\s*=\s*null\s*;\s*_cellarDoors\s*\?\s*\.\s*Dispose\s*\(\s*\)\s*;\s*_cellarDoors\s*=\s*null\s*;", RegexOptions.Singleline), plugin);
        // The trapdoors' install-failure path touches only the trapdoors.
        Assert.Matches(new Regex(@"try\s*\{\s*_trapdoors\s*\.\s*Dispose\s*\(\s*\)\s*;\s*\}\s*catch\s*\{\s*\}\s*_trapdoors\s*=\s*null\s*;\s*\}\s*\}"), plugin);
        // Its own switch: the cellar-door branch never reads the trapdoor setting.
        Assert.DoesNotMatch(new Regex(@"_trapdoorsEnabled[^;]*CellarDoorPieceRegistration"), plugin);

        foreach (string file in ProductFiles())
        {
            string relative = Relative(file);
            string text = Code(File.ReadAllText(file));
            if (relative != "Plugin.cs") { Assert.DoesNotMatch(new Regex(@"new\s+CellarDoorPieceRegistration\s*\("), text); }
            if (relative != Adapter) { Assert.DoesNotMatch(new Regex(@"CellarDoorLocalization\s*\.\s*Register\s*\("), text); }
            if (relative != "Pieces/CellarDoorLocalization.cs") { Assert.DoesNotMatch(new Regex(@"CellarDoors\s*\.\s*English\s*\("), text); }
        }

        Assert.Matches(new Regex(@"AddTranslation\s*\(\s*""English""\s*,\s*CellarDoors\s*\.\s*English\s*\(\s*\)\s*\)"),
            Code(Source("Pieces/CellarDoorLocalization.cs")));
    }

    [Fact]
    public void TheSettingSaysTheLeavesDoNotOpenYet()
    {
        Assert.False(CellarDoorSettings.EnabledByDefault);
        Assert.Contains("Their leaves do not open yet", CellarDoorSettings.Description, StringComparison.Ordinal);
    }

    // ---- The adapter's order and the shared helper -----------------------------------------------------------------

    [Fact]
    public void TheAdapterChecksTheDonorAndReadsTheCloneBackAgainstTheCellarCostBeforeAddingThePiece()
    {
        string adapter = Code(Source(Adapter));
        int donorCheck = Index(adapter, @"TrapdoorBlueprint\s*\.\s*DonorMismatches\s*\(\s*family\s*,\s*donor\s*==\s*null\s*\?\s*null\s*:\s*Read\s*\(\s*donor\s*\)\s*\)");
        int donorRefuse = Index(adapter, @"if\s*\(\s*problems\s*\.\s*Count\s*>\s*0\s*\)\s*\{\s*Refuse\s*\(");
        int clone = Index(adapter, @"new\s+CustomPiece\s*\(");
        int readBack = Index(adapter, @"TrapdoorBlueprint\s*\.\s*RequirementMismatches\s*\(\s*cellar\s*\.\s*Costs\s*,\s*Requirements\s*\(\s*piece\s*\)\s*\)");
        int readBackRefuse = Index(adapter, @"if\s*\(\s*recipe\s*\.\s*Count\s*>\s*0\s*\)\s*throw");
        int strip = Index(adapter, @"\bStrip\s*\(\s*prefab\s*\)\s*;");
        int verify = Index(adapter, @"\bVerify\s*\(\s*prefab\s*,\s*wear\s*\)\s*;");
        int add = Index(adapter, @"PieceManager\s*\.\s*Instance\s*\.\s*AddPiece\s*\(");
        Assert.True(donorCheck < donorRefuse && donorRefuse < clone && clone < readBack && readBack < readBackRefuse &&
                    readBackRefuse < strip && strip < verify && verify < add, "Add() lost its check order.");
        Assert.Single(Regex.Matches(adapter, @"new\s+CustomPiece\s*\("));
        Assert.Single(Regex.Matches(adapter, @"AddPiece\s*\("));
        // The requirements are the cellar door's own (doubled) cost, never the donor family's.
        Assert.Single(Regex.Matches(adapter, @"new\s+RequirementConfig\s*\(\s*cellar\s*\.\s*Costs\s*\[\s*index\s*\]\s*\.\s*Item\s*,\s*cellar\s*\.\s*Costs\s*\[\s*index\s*\]\s*\.\s*Amount\s*,\s*0\s*,\s*true\s*\)"));
        Assert.DoesNotMatch(new Regex(@"family\s*\.\s*Costs"), adapter);
        Assert.Matches(new Regex(@"var\s+cost\s*=\s*new\s+RequirementConfig\s*\[\s*cellar\s*\.\s*Costs\s*\.\s*Count\s*\]"), adapter);
        Assert.Matches(new Regex(@"Requirements\s*=\s*cost\b"), adapter);
        Assert.Matches(new Regex(@"foreach\s*\(\s*CellarDoorFamily\s+cellar\s+in\s+CellarDoors\s*\.\s*All\s*\)\s*\{\s*Add\s*\(\s*cellar\s*\)\s*;\s*\}"), adapter);
        Assert.Matches(new Regex(@"string\s+id\s*=\s*cellar\s*\.\s*Prefab\s*;"), adapter);
        Assert.Matches(new Regex(@"door\s*\.\s*m_name\s*=\s*""\$""\s*\+\s*cellar\s*\.\s*Token\s*;"), adapter);
        Assert.Matches(new Regex(@"Name\s*=\s*""\$""\s*\+\s*cellar\s*\.\s*Token\s*,"), adapter);
        Assert.Matches(new Regex(@"Description\s*=\s*""\$""\s*\+\s*CellarDoors\s*\.\s*DescriptionToken\s*,"), adapter);
        Assert.Matches(new Regex(@"Usage\s*=\s*CellarBlueprint\s*\.\s*Usage\s*\.\s*ToArray\s*\(\s*\)"), adapter);
    }

    [Fact]
    public void BothDoorAdaptersStripAndVerifyThroughTheOneSharedHelper()
    {
        foreach (string relative in new[] { Adapter, TrapdoorAdapter })
        {
            string adapter = Code(Source(relative));
            Assert.Matches(new Regex(@"using\s+static\s+TheConcernedCat\s*\.\s*ConcernedLadders\s*\.\s*Pieces\s*\.\s*DoorPieceKit\s*;"), adapter);
            Assert.Single(Regex.Matches(adapter, @"\bStrip\s*\(\s*prefab\s*\)\s*;"));
            Assert.Single(Regex.Matches(adapter, @"\bRequireRoot\s*\(\s*prefab\s*,"));
            Assert.Single(Regex.Matches(adapter, @"\bAdoptPresentation\s*\(\s*wear\s*,\s*fresh\s*,\s*worn\s*\)\s*;"));
            Assert.Single(Regex.Matches(adapter, @"\bVerifyStripped\s*\(\s*prefab\s*,\s*wear\s*,"));
            // A copy of the strip or of the wear wiring would be a second place for the two pieces to drift apart.
            Assert.DoesNotMatch(new Regex(@"DestroyImmediate|\bm_new\s*=|\bm_worn\s*=|\bm_broken\s*=|\bm_fragmentRoots\s*=|\bm_comOffset\s*=|private\s+static\s+(void\s+Strip|DonorFacts\s+Read)\s*\("), adapter);
        }

        string kit = Code(Source(Kit));
        Assert.Single(Regex.Matches(kit, @"internal\s+static\s+void\s+Strip\s*\(\s*GameObject\s+prefab\s*\)"));
        Assert.Single(Regex.Matches(kit, @"internal\s+static\s+DonorFacts\s+Read\s*\(\s*GameObject\s+donor\s*\)"));
        // The verification refuses any collider or renderer the adapter did not build, and a leaf that is not bare.
        Assert.Matches(new Regex(@"GetComponentsInChildren\s*<\s*Collider\s*>\s*\(\s*true\s*\)"), kit);
        Assert.Matches(new Regex(@"GetComponentsInChildren\s*<\s*Renderer\s*>\s*\(\s*true\s*\)"), kit);
        Assert.Matches(new Regex(@"leaf\s*\.\s*GetComponents\s*<\s*Component\s*>\s*\(\s*\)\s*\.\s*Length\s*!=\s*1"), kit);
    }

    [Fact]
    public void TheCellarDoorAddsNoNetworkStateNoRuntimeAndNoTrapdoorLid()
    {
        foreach (string relative in new[] { Adapter, Kit, "Pieces/CellarDoorLocalization.cs" })
        {
            string text = Code(Source(relative));
            Assert.DoesNotMatch(new Regex(@"\bZDO\b|GetZDO|ZDOVars|ZRoutedRpc|InvokeRPC|\bRegister\s*<|ClaimOwnership|SetOwner|\bm_nview\b|ZNetScene"), text);
            Assert.DoesNotMatch(new Regex(@"AddComponent\s*<\s*TrapdoorLid\s*>"), text);
        }

        // The adapter adds only the frame's mesh parts; the kit only the leaves' bodies and box colliders. No
        // MonoBehaviour: the leaves stay at their closed pose until the leaf runtime slice.
        Assert.Equal(new[] { "MeshCollider", "MeshFilter", "MeshRenderer" },
            Regex.Matches(Code(Source(Adapter)), @"AddComponent\s*<\s*(\w+)\s*>").Select(m => m.Groups[1].Value).Distinct().OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { "BoxCollider", "Rigidbody" },
            Regex.Matches(Code(Source(Kit)), @"AddComponent\s*<\s*(?:UnityEngine\s*\.\s*)?(\w+)\s*>").Select(m => m.Groups[1].Value).Distinct().OrderBy(n => n, StringComparer.Ordinal));
        string adapter = Code(Source(Adapter));
        // Two rotations are set, each once, at build time: the slope's tilt and each pivot's closed pose.
        Assert.Equal(2, Regex.Matches(adapter, @"\blocalRotation\s*=(?!=)").Count);
        Assert.Matches(new Regex(@"hinge\s*\.\s*transform\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*identity\s*;"), adapter);
        Assert.Matches(new Regex(@"slope\s*\.\s*transform\s*\.\s*localRotation\s*=\s*Quaternion\s*\.\s*Euler\s*\(\s*CellarBlueprint\s*\.\s*SlopeEulerX\s*,\s*0f\s*,\s*0f\s*\)\s*;"), adapter);
        // The clone is refused if it carries the trapdoor's lid runtime, which the ladders use to recognise a trapdoor.
        Assert.Matches(new Regex(@"GetComponentsInChildren\s*<\s*TrapdoorLid\s*>\s*\(\s*true\s*\)\s*\.\s*Length\s*!=\s*0"), adapter);
    }

    [Fact]
    public void TheFrameCollidersAreStaticConvexMeshesOfTheVisualsOwnMesh()
    {
        string adapter = Code(Source(Adapter));
        // Each leaf pivot gets its kinematic body and its leaf's box collider before the clone is verified.
        int body = Index(adapter, @"\bKinematicBody\s*\(\s*hinge\s*\)\s*;");
        int box = Index(adapter, @"\bColliderBox\s*\(\s*hinge\s*\.\s*transform\s*,\s*box\s*\.\s*Name\s*,");
        int verify = Index(adapter, @"\bVerify\s*\(\s*prefab\s*,\s*wear\s*\)\s*;");
        Assert.True(body < verify && box < verify, "The leaves' bodies and colliders must be on the clone before it is verified.");
        Assert.Matches(new Regex(@"collider\s*\.\s*attachedRigidbody\s*!=\s*null"), adapter);
        Assert.Matches(new Regex(@"collider\s*\.\s*convex\s*=\s*true\s*;\s*collider\s*\.\s*sharedMesh\s*=\s*FrameMesh\s*\(\s*shape\s*\)\s*;"), adapter);
        Assert.Matches(new Regex(@"AddComponent\s*<\s*MeshFilter\s*>\s*\(\s*\)\s*\.\s*sharedMesh\s*=\s*FrameMesh\s*\(\s*shape\s*\)\s*;"), adapter);
        Assert.Matches(new Regex(@"!\s*collider\s*\.\s*convex"), adapter);
        Assert.Matches(new Regex(@"mesh\s*\.\s*triangles\s*=\s*shape\s*\.\s*Triangles\s*\.\s*ToArray\s*\(\s*\)\s*;"), adapter);
        Assert.Matches(new Regex(@"mesh\s*\.\s*vertices\s*=\s*shape\s*\.\s*Vertices\s*\.\s*Select\s*\(\s*Vector\s*\)\s*\.\s*ToArray\s*\(\s*\)\s*;"), adapter);
        Assert.Matches(new Regex(@"foreach\s*\(\s*ClimbPoint\s+corner\s+in\s+CellarGeometry\s*\.\s*CornerSnaps\s*\)\s*Snap\s*\(\s*prefab\s*\.\s*transform\s*,\s*CellarGeometry\s*\.\s*CornerSnapToken\s*,"), adapter);
    }

    // ---- The frame meshes --------------------------------------------------------------------------------------------

    [Fact]
    public void EachFrameMeshIsItsPrismsEightCornersAsSixOutwardFaces()
    {
        Assert.Equal(CellarGeometry.Frame.Count, CellarBlueprint.FrameMeshes.Count);
        for (int index = 0; index < CellarGeometry.Frame.Count; ++index)
        {
            CellarPrism prism = CellarGeometry.Frame[index];
            CellarMesh mesh = CellarBlueprint.FrameMeshes[index];
            Assert.Equal(prism.Name, mesh.Name);
            Assert.Equal(24, mesh.Vertices.Count);
            Assert.Equal(24, mesh.Normals.Count);
            Assert.Equal(24, mesh.Uvs.Count);
            Assert.Equal(36, mesh.Triangles.Count);
            Assert.All(mesh.Triangles, t => Assert.InRange(t, 0, 23));
            ClimbPoint[] corners = Enumerable.Range(0, 8).Select(i => CellarBlueprint.Corner(prism, i)).ToArray();
            // Every vertex is a corner, and every corner is used: the mesh is exactly the prism, no more, no less.
            Assert.All(mesh.Vertices, v => Assert.Contains(corners, c => Same(c, v)));
            Assert.All(corners, c => Assert.Contains(mesh.Vertices, v => Same(c, v)));
            Assert.All(mesh.Uvs, uv => Assert.True(float.IsFinite(uv.U) && float.IsFinite(uv.V)));

            ClimbPoint centroid = CellarBlueprint.Centroid(prism);
            Assert.True(prism.ContainsStrictly(centroid, 0.001f), prism.Name + "'s centroid is not inside it.");
            for (int t = 0; t < 36; t += 3)
            {
                ClimbPoint a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
                ClimbPoint n = CellarBlueprint.TriangleNormal(a, b, c);
                float length = Length(n);
                Assert.True(length > 1e-4f, prism.Name + " has a degenerate triangle.");
                ClimbPoint middle = new ClimbPoint((a.X + b.X + c.X) / 3f - centroid.X, (a.Y + b.Y + c.Y) / 3f - centroid.Y, (a.Z + b.Z + c.Z) / 3f - centroid.Z);
                Assert.True(Dot(n, middle) > 0f, prism.Name + " triangle " + t / 3 + " faces into the prism (Unity would cull it from outside).");
                // The vertex normals are the triangle's own unit normal.
                foreach (int corner in new[] { t, t + 1, t + 2 })
                {
                    ClimbPoint normal = mesh.Normals[mesh.Triangles[corner]];
                    Assert.Equal(1f, Length(normal), 4);
                    Assert.Equal(1f, Dot(normal, n) / length, 4);
                }

                // Convex: every corner lies on or behind this triangle's plane.
                Assert.All(corners, p => Assert.True(Dot(n, Minus(p, a)) / length <= Epsilon, prism.Name + " is not convex at triangle " + t / 3));
            }
        }
    }

    [Fact]
    public void EachFrameMeshIsClosedConsistentlyWoundAndEnclosesThePrismsVolume()
    {
        foreach (CellarPrism prism in CellarGeometry.Frame)
        {
            CellarMesh mesh = CellarBlueprint.Mesh(prism);
            ClimbPoint[] corners = Enumerable.Range(0, 8).Select(i => CellarBlueprint.Corner(prism, i)).ToArray();
            int Key(int vertex) => Array.FindIndex(corners, c => Same(c, mesh.Vertices[vertex]));

            // Closed and consistently oriented: every directed edge appears once and its reverse once.
            var edges = new Dictionary<(int, int), int>();
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                for (int side = 0; side < 3; ++side)
                {
                    var edge = (Key(mesh.Triangles[t + side]), Key(mesh.Triangles[t + (side + 1) % 3]));
                    edges[edge] = edges.TryGetValue(edge, out int seen) ? seen + 1 : 1;
                }
            }

            foreach (KeyValuePair<(int, int), int> edge in edges)
            {
                Assert.Equal(1, edge.Value);
                Assert.True(edges.TryGetValue((edge.Key.Item2, edge.Key.Item1), out int reverse) && reverse == 1, prism.Name + " is open or inconsistently wound.");
            }

            // The divergence theorem: outward faces enclose exactly the profile's area times the prism's width.
            double volume = 0;
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                ClimbPoint a = mesh.Vertices[mesh.Triangles[t]], b = mesh.Vertices[mesh.Triangles[t + 1]], c = mesh.Vertices[mesh.Triangles[t + 2]];
                volume += Dot(a, CellarBlueprint.TriangleNormal(new ClimbPoint(0f, 0f, 0f), b, c)) / 6.0;
            }

            Assert.Equal(ProfileArea(prism) * (prism.MaxX - prism.MinX), volume, 4);
            Assert.True(volume > 0, prism.Name + " is inside out.");
        }
    }

    [Fact]
    public void AMeshFaceWoundInwardIsCaughtByTheOutwardTest()
    {
        // The test above must fail for a mesh that faces in: reverse one triangle of a real mesh and check the
        // outward rule notices.
        CellarMesh mesh = CellarBlueprint.FrameMeshes[0];
        CellarPrism prism = CellarGeometry.Frame[0];
        ClimbPoint centroid = CellarBlueprint.Centroid(prism);
        ClimbPoint a = mesh.Vertices[mesh.Triangles[0]], b = mesh.Vertices[mesh.Triangles[1]], c = mesh.Vertices[mesh.Triangles[2]];
        ClimbPoint middle = new ClimbPoint((a.X + b.X + c.X) / 3f - centroid.X, (a.Y + b.Y + c.Y) / 3f - centroid.Y, (a.Z + b.Z + c.Z) / 3f - centroid.Z);
        Assert.True(Dot(CellarBlueprint.TriangleNormal(a, b, c), middle) > 0f);
        Assert.True(Dot(CellarBlueprint.TriangleNormal(a, c, b), middle) < 0f);
    }

    // ---- The slope transform, pivots and leaves ------------------------------------------------------------------------

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    [InlineData(33f)]
    [InlineData(90f)]
    public void TheSlopeTransformPivotsAndLeafBoxesAreTheModelsLeaves(float angle)
    {
        Assert.Equal(-CellarGeometry.SlopeDegrees, CellarBlueprint.SlopeEulerX, 4);
        Assert.True(Same(CellarGeometry.SlopeOrigin, CellarBlueprint.SlopePosition));
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            CellarPart collider = CellarBlueprint.LeafCollider(leaf);
            CellarPart visual = CellarBlueprint.LeafVisual(leaf);
            Assert.True(Same(collider.Centre, visual.Centre) && Same(collider.Size, visual.Size), "What is seen is not what collides.");
            ClimbPoint[] built = BoxCorners(collider).Select(p => InPiece(leaf, p, angle)).ToArray();
            foreach (ClimbPoint corner in CellarGeometry.LeafCorners(leaf, angle))
            {
                Assert.Contains(built, b => Distance(b, corner) < 1e-4f);
            }
        }
    }

    [Fact]
    public void ThePartNamesAreDistinctAndNoneIsADonorOrTrapdoorName()
    {
        var names = new List<string> { CellarBlueprint.SlopeRoot, TrapdoorBlueprint.NewRoot, TrapdoorBlueprint.WornRoot };
        names.AddRange(CellarBlueprint.FrameMeshes.Select(m => m.Name));
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            names.Add(CellarBlueprint.Hinge(leaf));
            names.Add(CellarBlueprint.LeafCollider(leaf).Name);
            names.Add(CellarBlueprint.LeafVisual(leaf).Name);
            names.AddRange(CellarBlueprint.HingeMarks(leaf).Select(m => m.Name));
        }

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(TrapdoorBlueprint.LeafChild, names);
        Assert.DoesNotContain(TrapdoorBlueprint.Hinge, names);
        Assert.Equal(2, CellarBlueprint.Leaves.Count);
        Assert.Equal(new[] { "Building", "Doors" }, CellarBlueprint.Usage);
    }

    // ---- The hinge marks -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheHingeMarksSitOnEachLeafsTopAtItsHingeEdge()
    {
        Assert.True(CellarBlueprint.MarkThickness < CellarGeometry.LeafGap);
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            IReadOnlyList<CellarPart> marks = CellarBlueprint.HingeMarks(leaf);
            Assert.Equal(2, marks.Count);
            float inboard = leaf == CellarLeaf.Right ? -1f : 1f;
            foreach (CellarPart mark in marks)
            {
                ClimbPoint[] corners = BoxCorners(mark).ToArray();
                // On the top face (y 0 in pivot space), reaching from the hinge edge inboard.
                Assert.Equal(0f, corners.Min(p => p.Y), 4);
                Assert.Equal(0f, inboard > 0 ? corners.Min(p => p.X) : -corners.Max(p => p.X), 4);
                Assert.All(corners, p => Assert.True(inboard * p.X >= -Epsilon && inboard * p.X <= CellarGeometry.LeafWidth));
                // Along the leaf, inside its length.
                Assert.All(corners, p => Assert.True(Math.Abs(p.Z) <= CellarGeometry.LeafLength / 2f));
            }

            Assert.True(marks[0].Centre.Z < 0f && marks[1].Centre.Z > 0f);
        }
    }

    [Fact]
    public void AnOpeningLeafsHingeMarksNeverReachTheCheekOrTheSeam()
    {
        foreach (CellarLeaf leaf in CellarBlueprint.Leaves)
        {
            foreach (CellarPart mark in CellarBlueprint.HingeMarks(leaf))
            {
                for (float angle = 0f; angle <= 90f; angle += 0.5f)
                {
                    foreach (ClimbPoint p in BoxCorners(mark).Select(c => InPiece(leaf, c, angle)))
                    {
                        float across = Math.Abs(p.X);
                        Assert.True(across < CellarGeometry.HalfAperture, leaf + " mark reaches the cheek at " + angle + " degrees.");
                        Assert.True(across <= CellarGeometry.HingeU + CellarBlueprint.MarkThickness + Epsilon);
                        Assert.True(Math.Sign(p.X) == (leaf == CellarLeaf.Right ? 1 : -1), leaf + " mark crosses the seam at " + angle);
                    }
                }
            }
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------------------

    /// <summary>A pivot-space point in piece space: turned about the pivot's local z by the runtime convention, moved to
    /// the pivot in the slope transform, then through the slope transform (Unity's Euler about x and its origin).</summary>
    private static ClimbPoint InPiece(CellarLeaf leaf, ClimbPoint local, float angle)
    {
        double phi = CellarGeometry.HingeRotationSign(leaf) * angle * Math.PI / 180.0;
        float rx = (float)(local.X * Math.Cos(phi) - local.Y * Math.Sin(phi));
        float ry = (float)(local.X * Math.Sin(phi) + local.Y * Math.Cos(phi));
        ClimbPoint hinge = CellarBlueprint.HingePosition(leaf);
        float x = hinge.X + rx, y = hinge.Y + ry, z = hinge.Z + local.Z;
        double tilt = CellarBlueprint.SlopeEulerX * Math.PI / 180.0;
        float py = (float)(y * Math.Cos(tilt) - z * Math.Sin(tilt));
        float pz = (float)(y * Math.Sin(tilt) + z * Math.Cos(tilt));
        ClimbPoint origin = CellarBlueprint.SlopePosition;
        return new ClimbPoint(origin.X + x, origin.Y + py, origin.Z + pz);
    }

    private static IEnumerable<ClimbPoint> BoxCorners(CellarPart part)
    {
        for (int index = 0; index < 8; ++index)
        {
            yield return new ClimbPoint(
                part.Centre.X + ((index & 1) == 0 ? -0.5f : 0.5f) * part.Size.X,
                part.Centre.Y + ((index & 2) == 0 ? -0.5f : 0.5f) * part.Size.Y,
                part.Centre.Z + ((index & 4) == 0 ? -0.5f : 0.5f) * part.Size.Z);
        }
    }

    private static double ProfileArea(CellarPrism prism)
    {
        double area = 0;
        for (int index = 0; index < prism.Profile.Count; ++index)
        {
            ProfilePoint a = prism.Profile[index], b = prism.Profile[(index + 1) % prism.Profile.Count];
            area += a.Z * b.Y - b.Z * a.Y;
        }

        return Math.Abs(area) / 2.0;
    }

    private static bool Same(ClimbPoint a, ClimbPoint b) => Distance(a, b) < 1e-5f;

    private static float Distance(ClimbPoint a, ClimbPoint b) => Length(Minus(a, b));

    private static ClimbPoint Minus(ClimbPoint a, ClimbPoint b) => new ClimbPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static float Dot(ClimbPoint a, ClimbPoint b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static float Length(ClimbPoint a) => (float)Math.Sqrt(Dot(a, a));

    private static int Index(string text, string pattern)
    {
        Match match = Regex.Match(text, pattern);
        Assert.True(match.Success, "Missing: " + pattern);
        return match.Index;
    }

    private static string Code(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline), @"//[^\n]*", " ");

    private static string Relative(string file) => Path.GetRelativePath(ProductRoot, file).Replace('\\', '/');

    private static IEnumerable<string> ProductFiles() =>
        Directory.EnumerateFiles(ProductRoot, "*.cs", SearchOption.AllDirectories).Where(file =>
        {
            string relative = Relative(file);
            return !relative.StartsWith("obj/", StringComparison.Ordinal) && !relative.StartsWith("bin/", StringComparison.Ordinal);
        });

    private static string Source(string relative) => File.ReadAllText(Path.Combine(ProductRoot, relative));

    private static string ProductRoot => Path.Combine(RepoRoot, "src", "ConcernedLadders");

    private static string RepoRoot
    {
        get
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ConcernedCatMods.sln"))) { directory = directory.Parent; }
            return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }
}
