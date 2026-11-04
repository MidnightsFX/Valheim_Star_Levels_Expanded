using System;
using System.Collections.Generic;

namespace StarLevelSystem.modules.LevelSystem {
    internal enum RegionKind : byte { Land = 0, Mountain = 1 }

    // What to cut up. The grid is square and indexed z * Dim + x. Sizes are metres, the side of a square of that area.
    internal sealed class RegionBuildInput {
        internal int Dim;
        internal float CellSize;
        internal bool[] Land;
        internal bool[] Mountain;
        internal float TargetSize;
        internal float MinSize;
        internal float MountainMinSize;
    }

    internal struct BuiltRegion {
        internal ushort Id;
        internal RegionKind Kind;
        internal int CellCount;
    }

    internal sealed class RegionBuildResult {
        // The region each cell belongs to, 0 for none. Labels reach a little past the shore, so a creature in the shallows
        // still has a region; ClosedLand is the land itself.
        internal ushort[] Cells;
        internal bool[] ClosedLand;
        internal List<BuiltRegion> Regions;
    }

    // Cuts a sampled world into regions: each landmass is split around its mountains, and what is left is cut from seeds
    // spaced along its shoreline, growing inward so the sections come out about the same size.
    //
    // No game types and no shared state, because it runs on a worker thread once the world has been sampled.
    internal static class RegionBuilder {
        // Rivers run 60-100 m wide and would otherwise cut every island up along their banks.
        private const float RiverBridgeMeters = 75f;
        // Below this a landmass is a rock in the sea rather than somewhere creatures live, and gets no region.
        private const int MinIsletCells = 4;
        private const int RelaxPasses = 3;
        private const int SmoothPasses = 2;
        private const int ShoreDilatePasses = 2;

        private static readonly int[] Dx4 = { 1, -1, 0, 0 };
        private static readonly int[] Dz4 = { 0, 0, 1, -1 };
        private static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dz8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        internal static RegionBuildResult Build(RegionBuildInput input) {
            return new Job(input).Run();
        }

        // How many sections a piece of land is cut into: its share of the target size, but fewer rather than any under the
        // minimum, down to one larger section when even two would be too small.
        internal static int SectionsFor(int cells, int targetCells, int minCells) {
            int sections = Math.Max(1, (int)Math.Round(cells / (double)Math.Max(1, targetCells), MidpointRounding.AwayFromZero));
            while (sections > 1 && cells / sections < minCells) { sections--; }
            return sections;
        }

        private sealed class Piece {
            internal readonly List<int> Cells = new List<int>();
            internal RegionKind Kind;
        }

        private sealed class Job {
            private readonly int dim;
            private readonly int size;
            private readonly bool[] land;
            private readonly bool[] mountain;
            private readonly int targetCells;
            private readonly int minCells;
            private readonly int mountainMinCells;
            private readonly int closeRadius;

            // The region of each cell while it is being worked out, and each region's kind by id.
            private readonly int[] regionOf;
            private readonly List<RegionKind> kinds = new List<RegionKind> { RegionKind.Land };
            // Scratch that stays valid for a piece's cells only.
            private readonly int[] pieceOf;
            private readonly int[] scratch;
            private readonly int[] walkDistance;

            internal Job(RegionBuildInput input) {
                dim = input.Dim;
                size = dim * dim;
                land = (bool[])input.Land.Clone();
                mountain = input.Mountain;
                targetCells = Math.Max(1, CellsOfSide(input.TargetSize, input.CellSize));
                minCells = Math.Max(1, Math.Min(targetCells, CellsOfSide(input.MinSize, input.CellSize)));
                mountainMinCells = Math.Max(1, CellsOfSide(input.MountainMinSize, input.CellSize));
                closeRadius = Math.Max(1, (int)Math.Ceiling(RiverBridgeMeters / input.CellSize));
                regionOf = new int[size];
                pieceOf = new int[size];
                scratch = new int[size];
                walkDistance = new int[size];
            }

            private static int CellsOfSide(float meters, float cellSize) {
                double side = meters / cellSize;
                return (int)Math.Round(side * side);
            }

            internal RegionBuildResult Run() {
                CloseSmallGaps();
                FillEnclosedWater();

                List<Piece> pieces = FindPieces();
                for (int i = 0; i < pieces.Count; i++) {
                    foreach (int cell in pieces[i].Cells) { pieceOf[cell] = i + 1; }
                }
                for (int i = 0; i < pieces.Count; i++) { CutPiece(pieces[i], i + 1); }

                MergeUndersized();
                Smooth();
                KeepLargestParts();
                return Finish();
            }

            // Closing: grow the land, then shrink it back. Anything narrower than the river bridge stays filled in.
            private void CloseSmallGaps() {
                bool[] grown = Spread(land, closeRadius, true);
                bool[] shrunk = Spread(grown, closeRadius, false);
                Array.Copy(shrunk, land, size);
            }

            // A square kernel as two passes along the axes, so it costs the same whatever the radius.
            private bool[] Spread(bool[] from, int radius, bool value) {
                bool[] alongX = new bool[size];
                bool[] alongZ = new bool[size];
                for (int z = 0; z < dim; z++) {
                    int last = int.MinValue / 2;
                    for (int x = 0; x < dim; x++) {
                        if (from[z * dim + x] == value) { last = x; }
                        alongX[z * dim + x] = x - last <= radius;
                    }
                    last = int.MaxValue / 2;
                    for (int x = dim - 1; x >= 0; x--) {
                        if (from[z * dim + x] == value) { last = x; }
                        if (last - x <= radius) { alongX[z * dim + x] = true; }
                    }
                }
                for (int x = 0; x < dim; x++) {
                    int last = int.MinValue / 2;
                    for (int z = 0; z < dim; z++) {
                        if (alongX[z * dim + x]) { last = z; }
                        alongZ[z * dim + x] = z - last <= radius;
                    }
                    last = int.MaxValue / 2;
                    for (int z = dim - 1; z >= 0; z--) {
                        if (alongX[z * dim + x]) { last = z; }
                        if (last - z <= radius) { alongZ[z * dim + x] = true; }
                    }
                }
                if (value) { return alongZ; }
                // Shrinking is growing the water, so the marked cells are the ones that are not land.
                bool[] inverted = new bool[size];
                for (int i = 0; i < size; i++) { inverted[i] = alongZ[i] == false; }
                return inverted;
            }

            // Ponds and swamp pools inside the land belong to it. A sea that land happens to wrap around stays a sea: it is
            // told apart by being as large as a whole region.
            private void FillEnclosedWater() {
                bool[] seen = new bool[size];
                Queue<int> open = new Queue<int>();
                List<int> water = new List<int>();
                for (int start = 0; start < size; start++) {
                    if (land[start] || seen[start]) { continue; }
                    water.Clear();
                    bool enclosed = true;
                    seen[start] = true;
                    open.Enqueue(start);
                    while (open.Count > 0) {
                        int cell = open.Dequeue();
                        water.Add(cell);
                        int x = cell % dim, z = cell / dim;
                        if (x == 0 || z == 0 || x == dim - 1 || z == dim - 1) { enclosed = false; }
                        for (int d = 0; d < 4; d++) {
                            int nx = x + Dx4[d], nz = z + Dz4[d];
                            if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                            int next = nz * dim + nx;
                            if (land[next] || seen[next]) { continue; }
                            seen[next] = true;
                            open.Enqueue(next);
                        }
                    }
                    if (enclosed && water.Count < targetCells) {
                        foreach (int cell in water) { land[cell] = true; }
                    }
                }
            }

            // Every landmass, split into its mountains of a useful size and the connected land around them. A mountain too
            // small to stand alone stays part of that land.
            private List<Piece> FindPieces() {
                List<Piece> pieces = new List<Piece>();
                bool[] seen = new bool[size];
                bool[] standsAlone = new bool[size];
                // Landmasses never share a cell, so these serve for all of them.
                bool[] seenMountain = new bool[size];
                bool[] seenLand = new bool[size];
                foreach (List<int> mass in Flood(i => land[i], seen)) {
                    if (mass.Count < MinIsletCells) { continue; }
                    foreach (int cell in mass) { scratch[cell] = 1; }

                    foreach (List<int> blob in FloodCells(mass, i => scratch[i] == 1 && mountain[i], seenMountain)) {
                        if (blob.Count < mountainMinCells) { continue; }
                        Piece piece = new Piece { Kind = RegionKind.Mountain };
                        piece.Cells.AddRange(blob);
                        pieces.Add(piece);
                        foreach (int cell in blob) { standsAlone[cell] = true; }
                    }

                    foreach (List<int> rest in FloodCells(mass, i => scratch[i] == 1 && standsAlone[i] == false, seenLand)) {
                        Piece piece = new Piece { Kind = RegionKind.Land };
                        piece.Cells.AddRange(rest);
                        pieces.Add(piece);
                    }
                    foreach (int cell in mass) { scratch[cell] = 0; }
                }
                return pieces;
            }

            private IEnumerable<List<int>> Flood(Func<int, bool> within, bool[] seen) {
                for (int start = 0; start < size; start++) {
                    if (seen[start] || within(start) == false) { continue; }
                    yield return FloodFrom(start, within, seen);
                }
            }

            private IEnumerable<List<int>> FloodCells(List<int> cells, Func<int, bool> within, bool[] seen) {
                foreach (int start in cells) {
                    if (seen[start] || within(start) == false) { continue; }
                    yield return FloodFrom(start, within, seen);
                }
            }

            private List<int> FloodFrom(int start, Func<int, bool> within, bool[] seen) {
                List<int> found = new List<int>();
                Queue<int> open = new Queue<int>();
                seen[start] = true;
                open.Enqueue(start);
                while (open.Count > 0) {
                    int cell = open.Dequeue();
                    found.Add(cell);
                    int x = cell % dim, z = cell / dim;
                    for (int d = 0; d < 4; d++) {
                        int nx = x + Dx4[d], nz = z + Dz4[d];
                        if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                        int next = nz * dim + nx;
                        if (seen[next] || within(next) == false) { continue; }
                        seen[next] = true;
                        open.Enqueue(next);
                    }
                }
                return found;
            }

            private int NewRegion(RegionKind kind) {
                kinds.Add(kind);
                return kinds.Count - 1;
            }

            private void CutPiece(Piece piece, int pieceId) {
                int sections = SectionsFor(piece.Cells.Count, targetCells, minCells);
                if (sections <= 1) {
                    int region = NewRegion(piece.Kind);
                    foreach (int cell in piece.Cells) { regionOf[cell] = region; }
                    return;
                }

                int[] seeds = ShoreSeeds(piece, pieceId, sections);
                Grow(piece, pieceId, seeds);
                for (int pass = 0; pass < RelaxPasses; pass++) {
                    MoveSeedsToCentres(piece, seeds);
                    Grow(piece, pieceId, seeds);
                }

                int[] regions = new int[seeds.Length];
                for (int i = 0; i < regions.Length; i++) { regions[i] = NewRegion(piece.Kind); }
                foreach (int cell in piece.Cells) { regionOf[cell] = regions[scratch[cell]]; }
                foreach (int cell in piece.Cells) { scratch[cell] = 0; }
            }

            // Walks the piece's shoreline, each seed as far along it as it can get from the seeds before it. Measured along
            // the coast, a long thin peninsula gets a seed of its own however close its tip is to the mainland in a straight
            // line. Land walled in by mountains has no shore, so its edge stands in for one.
            private int[] ShoreSeeds(Piece piece, int pieceId, int count) {
                List<int> coast = new List<int>();
                foreach (int cell in piece.Cells) {
                    if (TouchesWater(cell)) { coast.Add(cell); }
                }
                if (coast.Count < count) {
                    coast.Clear();
                    foreach (int cell in piece.Cells) {
                        if (TouchesAnotherPiece(cell, pieceId)) { coast.Add(cell); }
                    }
                }
                if (coast.Count < count) { coast = piece.Cells; }

                const int Far = int.MaxValue;
                foreach (int cell in piece.Cells) { walkDistance[cell] = -1; }
                foreach (int cell in coast) { walkDistance[cell] = Far; }

                int[] seeds = new int[count];
                int next = coast[0];
                Queue<int> open = new Queue<int>();
                for (int s = 0; s < count; s++) {
                    seeds[s] = next;
                    walkDistance[next] = 0;
                    open.Enqueue(next);
                    while (open.Count > 0) {
                        int cell = open.Dequeue();
                        int walked = walkDistance[cell] + 1;
                        int x = cell % dim, z = cell / dim;
                        for (int d = 0; d < 8; d++) {
                            int nx = x + Dx8[d], nz = z + Dz8[d];
                            if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                            int neighbour = nz * dim + nx;
                            if (pieceOf[neighbour] != pieceId || walkDistance[neighbour] <= walked) { continue; }
                            walkDistance[neighbour] = walked;
                            open.Enqueue(neighbour);
                        }
                    }
                    // A stretch the walk could not reach counts as the farthest of all, so it gets the next seed.
                    int farthest = -1;
                    foreach (int cell in coast) {
                        if (walkDistance[cell] > farthest) {
                            farthest = walkDistance[cell];
                            next = cell;
                        }
                    }
                }
                return seeds;
            }

            private bool TouchesWater(int cell) {
                int x = cell % dim, z = cell / dim;
                for (int d = 0; d < 4; d++) {
                    int nx = x + Dx4[d], nz = z + Dz4[d];
                    if (nx < 0 || nz < 0 || nx >= dim || nz >= dim || land[nz * dim + nx] == false) { return true; }
                }
                return false;
            }

            private bool TouchesAnotherPiece(int cell, int pieceId) {
                int x = cell % dim, z = cell / dim;
                for (int d = 0; d < 4; d++) {
                    int nx = x + Dx4[d], nz = z + Dz4[d];
                    if (nx < 0 || nz < 0 || nx >= dim || nz >= dim || pieceOf[nz * dim + nx] != pieceId) { return true; }
                }
                return false;
            }

            // Fills the piece from the seeds, always growing whichever section is smallest, so they come out about equal
            // unless one is boxed in. The section of each cell ends up in scratch.
            private void Grow(Piece piece, int pieceId, int[] seeds) {
                foreach (int cell in piece.Cells) { scratch[cell] = -1; }
                int count = seeds.Length;
                Queue<int>[] frontier = new Queue<int>[count];
                int[] area = new int[count];
                for (int s = 0; s < count; s++) {
                    frontier[s] = new Queue<int>();
                    frontier[s].Enqueue(seeds[s]);
                    scratch[seeds[s]] = s;
                    area[s] = 1;
                }
                while (true) {
                    int smallest = -1;
                    for (int s = 0; s < count; s++) {
                        if (frontier[s].Count > 0 && (smallest < 0 || area[s] < area[smallest])) { smallest = s; }
                    }
                    if (smallest < 0) { break; }
                    int cell = frontier[smallest].Dequeue();
                    int x = cell % dim, z = cell / dim;
                    for (int d = 0; d < 4; d++) {
                        int nx = x + Dx4[d], nz = z + Dz4[d];
                        if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                        int next = nz * dim + nx;
                        if (pieceOf[next] != pieceId || scratch[next] != -1) { continue; }
                        scratch[next] = smallest;
                        area[smallest]++;
                        frontier[smallest].Enqueue(next);
                    }
                }
            }

            // Each seed moves to the cell nearest its section's centre, which pulls sections off the coast they started on:
            // without this a large island comes out as long slices all meeting in the middle.
            private void MoveSeedsToCentres(Piece piece, int[] seeds) {
                int count = seeds.Length;
                long[] sumX = new long[count];
                long[] sumZ = new long[count];
                int[] area = new int[count];
                foreach (int cell in piece.Cells) {
                    int s = scratch[cell];
                    sumX[s] += cell % dim;
                    sumZ[s] += cell / dim;
                    area[s]++;
                }
                long[] nearest = new long[count];
                for (int s = 0; s < count; s++) { nearest[s] = long.MaxValue; }
                foreach (int cell in piece.Cells) {
                    int s = scratch[cell];
                    if (area[s] == 0) { continue; }
                    long dx = (cell % dim) * area[s] - sumX[s];
                    long dz = (cell / dim) * area[s] - sumZ[s];
                    long distance = dx * dx + dz * dz;
                    if (distance < nearest[s]) {
                        nearest[s] = distance;
                        seeds[s] = cell;
                    }
                }
            }

            private bool TooSmall(int region, int cells) {
                return cells < (kinds[region] == RegionKind.Mountain ? mountainMinCells : minCells);
            }

            // A piece cut off by a mountain can be smaller than any section should be. It joins the neighbour it shares the
            // longest border with, land for preference. One with no neighbour at all is an island, and stays.
            private void MergeUndersized() {
                int regions = kinds.Count;
                int[] cells = new int[regions];
                Dictionary<int, int>[] borders = new Dictionary<int, int>[regions];
                for (int i = 0; i < regions; i++) { borders[i] = new Dictionary<int, int>(); }
                for (int z = 0; z < dim; z++) {
                    for (int x = 0; x < dim; x++) {
                        int here = regionOf[z * dim + x];
                        if (here == 0) { continue; }
                        cells[here]++;
                        if (x + 1 < dim) { AddBorder(borders, here, regionOf[z * dim + x + 1]); }
                        if (z + 1 < dim) { AddBorder(borders, here, regionOf[(z + 1) * dim + x]); }
                    }
                }

                int[] mergedInto = new int[regions];
                while (true) {
                    int smallest = -1;
                    for (int r = 1; r < regions; r++) {
                        if (mergedInto[r] != 0 || borders[r].Count == 0 || TooSmall(r, cells[r]) == false) { continue; }
                        if (smallest < 0 || cells[r] < cells[smallest]) { smallest = r; }
                    }
                    if (smallest < 0) { break; }

                    int into = -1;
                    foreach (KeyValuePair<int, int> border in borders[smallest]) {
                        if (into < 0) { into = border.Key; continue; }
                        bool sameKind = kinds[border.Key] == kinds[smallest];
                        bool intoSameKind = kinds[into] == kinds[smallest];
                        if ((sameKind && intoSameKind == false) || (sameKind == intoSameKind && border.Value > borders[smallest][into])) { into = border.Key; }
                    }

                    mergedInto[smallest] = into;
                    cells[into] += cells[smallest];
                    foreach (KeyValuePair<int, int> border in borders[smallest]) {
                        borders[border.Key].Remove(smallest);
                        if (border.Key == into) { continue; }
                        borders[into].TryGetValue(border.Key, out int shared);
                        borders[into][border.Key] = shared + border.Value;
                        borders[border.Key][into] = shared + border.Value;
                    }
                    borders[smallest].Clear();
                }

                for (int i = 0; i < size; i++) {
                    int region = regionOf[i];
                    while (region != 0 && mergedInto[region] != 0) { region = mergedInto[region]; }
                    regionOf[i] = region;
                }
            }

            private static void AddBorder(Dictionary<int, int>[] borders, int a, int b) {
                if (b == 0 || a == b) { return; }
                borders[a].TryGetValue(b, out int shared);
                borders[a][b] = shared + 1;
                borders[b][a] = shared + 1;
            }

            // Takes the stair-steps out of the edges between sections: a cell mostly surrounded by one other section joins it.
            private void Smooth() {
                int[] counts = new int[kinds.Count];
                int[] next = new int[size];
                for (int pass = 0; pass < SmoothPasses; pass++) {
                    Array.Copy(regionOf, next, size);
                    for (int z = 0; z < dim; z++) {
                        for (int x = 0; x < dim; x++) {
                            int cell = z * dim + x;
                            int here = regionOf[cell];
                            if (here == 0) { continue; }
                            for (int d = 0; d < 8; d++) {
                                int nx = x + Dx8[d], nz = z + Dz8[d];
                                if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                                int other = regionOf[nz * dim + nx];
                                if (other == 0 || other == here) { continue; }
                                if (++counts[other] >= 5) { next[cell] = other; }
                            }
                            for (int d = 0; d < 8; d++) {
                                int nx = x + Dx8[d], nz = z + Dz8[d];
                                if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                                counts[regionOf[nz * dim + nx]] = 0;
                            }
                        }
                    }
                    Array.Copy(next, regionOf, size);
                }
            }

            // Smoothing can pinch a section in two. Each keeps its largest part, and the rest go to whatever they touch, so
            // a region is always one connected area.
            private void KeepLargestParts() {
                bool[] seen = new bool[size];
                int[] largest = new int[kinds.Count];
                List<List<int>> parts = new List<List<int>>();
                for (int start = 0; start < size; start++) {
                    int region = regionOf[start];
                    if (region == 0 || seen[start]) { continue; }
                    List<int> part = FloodFrom(start, i => regionOf[i] == region, seen);
                    parts.Add(part);
                    if (part.Count > largest[region]) { largest[region] = part.Count; }
                }

                List<int> orphans = new List<int>();
                bool[] kept = new bool[kinds.Count];
                foreach (List<int> part in parts) {
                    int region = regionOf[part[0]];
                    if (part.Count == largest[region] && kept[region] == false) {
                        kept[region] = true;
                        continue;
                    }
                    orphans.AddRange(part);
                }
                foreach (int cell in orphans) { regionOf[cell] = 0; }

                bool changed = true;
                while (changed && orphans.Count > 0) {
                    changed = false;
                    for (int i = orphans.Count - 1; i >= 0; i--) {
                        int cell = orphans[i];
                        int neighbour = LabelledNeighbour(cell);
                        if (neighbour == 0) { continue; }
                        regionOf[cell] = neighbour;
                        orphans[i] = orphans[orphans.Count - 1];
                        orphans.RemoveAt(orphans.Count - 1);
                        changed = true;
                    }
                }
            }

            private int LabelledNeighbour(int cell) {
                int x = cell % dim, z = cell / dim;
                for (int d = 0; d < 4; d++) {
                    int nx = x + Dx4[d], nz = z + Dz4[d];
                    if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                    int other = regionOf[nz * dim + nx];
                    if (other != 0) { return other; }
                }
                return 0;
            }

            // Ids run from 1 with no gaps, in the order the grid reaches them. Only then do labels spread past the shore, so
            // a region's cell count stays its land.
            private RegionBuildResult Finish() {
                int[] finalId = new int[kinds.Count];
                List<BuiltRegion> regions = new List<BuiltRegion>();
                int[] counts = new int[kinds.Count];
                for (int i = 0; i < size; i++) {
                    int region = regionOf[i];
                    if (region == 0) { continue; }
                    counts[region]++;
                    if (finalId[region] == 0) {
                        // The grid cannot name more than this. The size settings keep real worlds far below it.
                        if (regions.Count >= ushort.MaxValue) { regionOf[i] = 0; continue; }
                        finalId[region] = regions.Count + 1;
                        regions.Add(new BuiltRegion { Id = (ushort)(regions.Count + 1), Kind = kinds[region] });
                    }
                }
                for (int r = 1; r < kinds.Count; r++) {
                    if (finalId[r] == 0) { continue; }
                    BuiltRegion built = regions[finalId[r] - 1];
                    built.CellCount = counts[r];
                    regions[finalId[r] - 1] = built;
                }

                ushort[] cells = new ushort[size];
                for (int i = 0; i < size; i++) {
                    if (regionOf[i] != 0) { cells[i] = (ushort)finalId[regionOf[i]]; }
                }
                ushort[] next = new ushort[size];
                for (int pass = 0; pass < ShoreDilatePasses; pass++) {
                    Array.Copy(cells, next, size);
                    for (int z = 0; z < dim; z++) {
                        for (int x = 0; x < dim; x++) {
                            int cell = z * dim + x;
                            // Land still without a region is a rock too small for one, and stays that way.
                            if (cells[cell] != 0 || land[cell]) { continue; }
                            for (int d = 0; d < 4; d++) {
                                int nx = x + Dx4[d], nz = z + Dz4[d];
                                if (nx < 0 || nz < 0 || nx >= dim || nz >= dim) { continue; }
                                ushort other = cells[nz * dim + nx];
                                if (other != 0) {
                                    next[cell] = other;
                                    break;
                                }
                            }
                        }
                    }
                    Array.Copy(next, cells, size);
                }
                return new RegionBuildResult { Cells = cells, ClosedLand = land, Regions = regions };
            }
        }
    }
}
