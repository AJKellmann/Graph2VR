using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Deliberately independent of Unity: coordinates remain in Angstroms.
public sealed class PdbStructure
{
  public sealed class Atom
  {
    public int Serial, Segment;
    public string Name, Element, Residue, ResidueId, Chain;
    public char Alternate;
    public float X, Y, Z, Occupancy;
    public string ResidueKey => Segment + ":" + Chain + ":" + ResidueId;
  }

  public readonly struct Bond
  {
    public readonly int First, Second;
    public Bond(int first, int second) { First = first; Second = second; }
  }

  public readonly List<Atom> Atoms = new List<Atom>();
  public readonly List<Bond> Bonds = new List<Bond>();
  public readonly List<string> Warnings = new List<string>();
  public const int MaximumAtoms = 10000;
  public const int MaximumTextLength = 8 * 1024 * 1024;

  private static readonly Dictionary<string, string> SideChains = new Dictionary<string, string>
  {
    { "GLY", "" }, { "ALA", "CA-CB" },
    { "VAL", "CA-CB CB-CG1 CB-CG2" },
    { "LEU", "CA-CB CB-CG CG-CD1 CG-CD2" },
    { "ILE", "CA-CB CB-CG1 CB-CG2 CG1-CD1" },
    { "SER", "CA-CB CB-OG" }, { "THR", "CA-CB CB-OG1 CB-CG2" },
    { "CYS", "CA-CB CB-SG" }, { "MET", "CA-CB CB-CG CG-SD SD-CE" },
    { "ASP", "CA-CB CB-CG CG-OD1 CG-OD2" },
    { "ASN", "CA-CB CB-CG CG-OD1 CG-ND2" },
    { "GLU", "CA-CB CB-CG CG-CD CD-OE1 CD-OE2" },
    { "GLN", "CA-CB CB-CG CG-CD CD-OE1 CD-NE2" },
    { "LYS", "CA-CB CB-CG CG-CD CD-CE CE-NZ" },
    { "ARG", "CA-CB CB-CG CG-CD CD-NE NE-CZ CZ-NH1 CZ-NH2" },
    { "HIS", "CA-CB CB-CG CG-ND1 ND1-CE1 CE1-NE2 NE2-CD2 CD2-CG" },
    { "PHE", "CA-CB CB-CG CG-CD1 CD1-CE1 CE1-CZ CZ-CE2 CE2-CD2 CD2-CG" },
    { "TYR", "CA-CB CB-CG CG-CD1 CD1-CE1 CE1-CZ CZ-CE2 CE2-CD2 CD2-CG CZ-OH" },
    { "TRP", "CA-CB CB-CG CG-CD1 CD1-NE1 NE1-CE2 CE2-CD2 CD2-CG CD2-CE3 CE3-CZ3 CZ3-CH2 CH2-CZ2 CZ2-CE2" },
    { "PRO", "N-CD CD-CG CG-CB CB-CA" }
  };

  public static PdbStructure Parse(string text)
  {
    if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Empty PDB file.");
    if (text.Length > MaximumTextLength) throw new FormatException("PDB file exceeds the 8 MiB text limit.");
    var result = new PdbStructure();
    var residues = new Dictionary<string, List<Atom>>();
    var residueOrder = new List<string>();
    var connections = new List<Tuple<int, int>>();
    int segment = 0, modelCount = 0, lineNumber = 0, candidates = 0;
    bool acceptAtoms = true, insideModel = false;
    using (var reader = new StringReader(text))
    {
      string line;
      while ((line = reader.ReadLine()) != null)
      {
        lineNumber++;
        string record = Field(line, 0, 6).Trim();
        if (record == "MODEL") { modelCount++; insideModel = true; acceptAtoms = modelCount == 1; continue; }
        if (record == "ENDMDL") { acceptAtoms = false; insideModel = false; continue; }
        if (record == "TER" && acceptAtoms) { segment++; continue; }
        if (record == "CONECT" && (!insideModel || acceptAtoms))
        {
          int source = Integer(Field(line, 6, 5), lineNumber);
          for (int offset = 11; offset < Math.Min(line.Length, 31); offset += 5)
          {
            string target = Field(line, offset, 5).Trim();
            if (target.Length > 0) connections.Add(Tuple.Create(source, Integer(target, lineNumber)));
          }
          continue;
        }
        if ((record != "ATOM" && record != "HETATM") || !acceptAtoms) continue;
        if (++candidates > MaximumAtoms * 4) throw new FormatException("Too many atom/alternate records.");
        string residue = Field(line, 17, 3).Trim().ToUpperInvariant();
        if (residue == "HOH" || residue == "WAT" || residue == "DOD") continue;
        if (line.Length < 54) throw new FormatException("Incomplete atom at line " + lineNumber);
        string rawName = Field(line, 12, 4);
        string element = Field(line, 76, 2).Trim().ToUpperInvariant();
        if (element.Length == 0) element = InferElement(rawName);
        var atom = new Atom {
          Serial = Integer(Field(line, 6, 5), lineNumber), Name = rawName.Trim(),
          Element = element, Residue = residue, ResidueId = Field(line, 22, 5).Trim(),
          Chain = Field(line, 21, 1), Segment = segment,
          Alternate = line[16], X = Number(Field(line, 30, 8), lineNumber),
          Y = Number(Field(line, 38, 8), lineNumber), Z = Number(Field(line, 46, 8), lineNumber),
          Occupancy = string.IsNullOrWhiteSpace(Field(line, 54, 6)) ? 1 : Number(Field(line, 54, 6), lineNumber)
        };
        if (!residues.TryGetValue(atom.ResidueKey, out List<Atom> group))
        {
          group = new List<Atom>(); residues.Add(atom.ResidueKey, group); residueOrder.Add(atom.ResidueKey);
        }
        group.Add(atom);
      }
    }
    if (modelCount > 1) result.Warnings.Add("Only the first MODEL is displayed.");
    foreach (string key in residueOrder)
    {
      List<Atom> group = residues[key];
      var scores = new SortedDictionary<char, float>();
      foreach (Atom atom in group)
        if (atom.Alternate != ' ')
          scores[atom.Alternate] = (scores.TryGetValue(atom.Alternate, out float score) ? score : 0) + atom.Occupancy;
      char chosen = ' '; float best = -1;
      foreach (var score in scores) if (score.Value > best) { chosen = score.Key; best = score.Value; }
      var names = new HashSet<string>();
      // Blank positions are shared between conformers; choose one alternate for the entire residue.
      foreach (Atom atom in group)
        if (atom.Alternate == ' ' && names.Add(atom.Name)) result.Atoms.Add(atom);
      foreach (Atom atom in group)
        if (atom.Alternate == chosen && names.Add(atom.Name)) result.Atoms.Add(atom);
    }
    if (result.Atoms.Count == 0) throw new FormatException("No supported atoms in the first PDB model.");
    if (result.Atoms.Count > MaximumAtoms) throw new FormatException("PDB exceeds the 10,000 atom display limit.");
    result.BuildBonds(connections);
    return result;
  }

  private void BuildBonds(List<Tuple<int, int>> connections)
  {
    var serials = new Dictionary<int, int>();
    var groups = new Dictionary<string, Dictionary<string, int>>();
    var order = new List<string>();
    var bonds = new HashSet<long>();
    var sulfurs = new List<int>();
    for (int i = 0; i < Atoms.Count; i++)
    {
      Atom atom = Atoms[i];
      if (serials.ContainsKey(atom.Serial)) throw new FormatException("Duplicate atom serial in first model: " + atom.Serial);
      serials.Add(atom.Serial, i);
      if (!groups.TryGetValue(atom.ResidueKey, out Dictionary<string, int> group))
      { group = new Dictionary<string, int>(); groups.Add(atom.ResidueKey, group); order.Add(atom.ResidueKey); }
      group[atom.Name] = i;
      if (atom.Residue == "CYS" && atom.Name == "SG") sulfurs.Add(i);
    }
    foreach (var pair in connections)
      if (serials.TryGetValue(pair.Item1, out int a) && serials.TryGetValue(pair.Item2, out int b)) AddBond(a, b, bonds);
    var unknown = new HashSet<string>();
    Dictionary<string, int> previous = null;
    Atom previousAtom = null;
    foreach (string key in order)
    {
      Dictionary<string, int> group = groups[key];
      Atom first = null;
      foreach (int index in group.Values) { first = Atoms[index]; break; }
      if (SideChains.TryGetValue(first.Residue, out string side))
      {
        foreach (string pair in ("N-CA CA-C C-O C-OXT " + side).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
          string[] ends = pair.Split('-');
          if (group.TryGetValue(ends[0], out int a) && group.TryGetValue(ends[1], out int b) && DistanceSquared(a, b) < 4.84f)
            AddBond(a, b, bonds);
        }
        if (previous != null && previousAtom.Chain == first.Chain && previousAtom.Segment == first.Segment &&
            previous.TryGetValue("C", out int carbon) && group.TryGetValue("N", out int nitrogen) &&
            DistanceSquared(carbon, nitrogen) < 3.61f)
          AddBond(carbon, nitrogen, bonds);
        // Present hydrogens only; do not synthesize missing atoms or infer bond orders.
        foreach (int h in group.Values)
        {
          if (Atoms[h].Element != "H" && Atoms[h].Element != "D") continue;
          int nearest = -1; float distance = 2.25f;
          foreach (int heavy in group.Values)
          {
            string element = Atoms[heavy].Element;
            if (element != "C" && element != "N" && element != "O" && element != "S") continue;
            float candidate = DistanceSquared(h, heavy);
            if (candidate < distance && candidate > 0.16f) { distance = candidate; nearest = heavy; }
          }
          if (nearest >= 0) AddBond(h, nearest, bonds);
        }
        previous = group; previousAtom = first;
      }
      else { unknown.Add(first.Residue); previous = null; }
    }
    // Conservative geometric fallback for cysteine disulfides, including between chains.
    // Ambiguous sulfur contacts are left to explicit CONECT records.
    var neighbors = new Dictionary<int, List<int>>();
    foreach (int sulfur in sulfurs) neighbors[sulfur] = new List<int>();
    for (int i = 0; i < sulfurs.Count; i++)
      for (int j = i + 1; j < sulfurs.Count; j++)
      {
        float distance = DistanceSquared(sulfurs[i], sulfurs[j]);
        if (distance >= 3.24f && distance <= 5.29f)
        { neighbors[sulfurs[i]].Add(sulfurs[j]); neighbors[sulfurs[j]].Add(sulfurs[i]); }
      }
    foreach (int sulfur in sulfurs)
      if (neighbors[sulfur].Count == 1 && neighbors[neighbors[sulfur][0]].Count == 1)
        AddBond(sulfur, neighbors[sulfur][0], bonds);
    if (unknown.Count > 0) Warnings.Add("Only explicit CONECT bonds for nonstandard residues: " + string.Join(", ", unknown));
  }

  private void AddBond(int a, int b, HashSet<long> bonds)
  {
    if (a == b || DistanceSquared(a, b) < 0.01f) return;
    int low = Math.Min(a, b), high = Math.Max(a, b);
    if (bonds.Add(((long)low << 32) | (uint)high)) Bonds.Add(new Bond(low, high));
  }

  private float DistanceSquared(int a, int b)
  {
    float x = Atoms[a].X - Atoms[b].X, y = Atoms[a].Y - Atoms[b].Y, z = Atoms[a].Z - Atoms[b].Z;
    return x * x + y * y + z * z;
  }

  private static string InferElement(string name)
  {
    // PDB alignment distinguishes alpha carbon (" CA ") from calcium ("CA  ").
    if (name.Length == 0) return "X";
    if (name[0] == ' ' || char.IsDigit(name[0]))
      foreach (char c in name) if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
    string letters = name.Trim();
    if (letters.Length > 1 && char.IsLetter(letters[1])) return letters.Substring(0, 2).ToUpperInvariant();
    return letters.Length > 0 ? letters.Substring(0, 1).ToUpperInvariant() : "X";
  }

  private static string Field(string line, int start, int length) => start >= line.Length ? "" : line.Substring(start, Math.Min(length, line.Length - start));
  private static int Integer(string text, int line)
  {
    if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
      throw new FormatException("Invalid integer at PDB line " + line);
    return value;
  }
  private static float Number(string text, int line)
  {
    if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1000000)
      throw new FormatException("Invalid coordinate/occupancy at PDB line " + line);
    return value;
  }
}
