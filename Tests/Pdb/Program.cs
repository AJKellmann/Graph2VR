using System;
using System.Globalization;
using System.IO;
using System.Linq;

static class Program
{
  static int checks;
  static void Check(bool condition, string message)
  { if (!condition) throw new Exception(message); checks++; }
  static string Atom(int serial, string name, string residue, int sequence, float x, float y = 0, float z = 0,
    string element = "C", char alternate = ' ', float occupancy = 1, string chain = "A")
  {
    return string.Format(CultureInfo.InvariantCulture,
      "ATOM  {0,5} {1}{2}{3,3} {4}{5,4}    {6,8:F3}{7,8:F3}{8,8:F3}{9,6:F2}{10,6:F2}          {11,2}",
      serial, name.PadRight(4), alternate, residue, chain, sequence, x, y, z, occupancy, 0, element);
  }
  static PdbStructure Parse(params string[] lines) => PdbStructure.Parse(string.Join("\n", lines));
  static bool Bond(PdbStructure s, int a, int b) => s.Bonds.Any(p =>
    (s.Atoms[p.First].Serial == a && s.Atoms[p.Second].Serial == b) ||
    (s.Atoms[p.First].Serial == b && s.Atoms[p.Second].Serial == a));
  static void Reject(string text)
  { try { PdbStructure.Parse(text); } catch (FormatException) { checks++; return; } throw new Exception("Expected invalid PDB rejection"); }

  static void Main(string[] args)
  {
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    var gly = Parse(Atom(1, " N", "GLY", 1, 0, element: "N"), Atom(2, " CA", "GLY", 1, 1.45f),
      Atom(3, " C", "GLY", 1, 2.8f), Atom(4, " O", "GLY", 1, 3.9f, element: "O"));
    Check(gly.Atoms.Count == 4 && gly.Bonds.Count == 3, "Glycine topology");
    Check(Math.Abs(gly.Atoms[1].X - 1.45f) < 0.0001f, "Invariant decimal parsing");
    var peptide = Parse(Atom(1, " C", "GLY", 1, 0), Atom(2, " N", "ALA", 2, 1.33f, element: "N"));
    Check(Bond(peptide, 1, 2), "Peptide bond");
    var broken = Parse(Atom(1, " C", "GLY", 1, 0), "TER", Atom(2, " N", "ALA", 2, 1.33f, element: "N"));
    Check(broken.Bonds.Count == 0, "TER breaks connectivity");
    var gap = Parse(Atom(1, " C", "GLY", 1, 0), Atom(2, " N", "ALA", 2, 10, element: "N"));
    Check(gap.Bonds.Count == 0, "Missing coordinates do not bridge a gap");
    var chains = Parse(Atom(1, " C", "GLY", 1, 0), Atom(2, " N", "ALA", 2, 1.33f, element: "N", chain: "B"));
    Check(chains.Bonds.Count == 0, "No peptide bonds between chains");
    var cys = Parse(Atom(1, " SG", "CYS", 1, 0, element: "S"), Atom(2, " SG", "CYS", 2, 2.05f, element: "S", chain: "B"));
    Check(Bond(cys, 1, 2), "Interchain disulfide");
    var ambiguous = Parse(Atom(1, " SG", "CYS", 1, 0, element: "S"), Atom(2, " SG", "CYS", 2, 2.05f, element: "S"),
      Atom(3, " SG", "CYS", 3, -2.05f, element: "S"));
    Check(ambiguous.Bonds.Count == 0, "Ambiguous sulfur contacts are not guessed");
    var alt = Parse(Atom(1, " CA", "ALA", 1, 0, alternate: 'A', occupancy: 0.3f),
      Atom(2, " CA", "ALA", 1, 1, alternate: 'B', occupancy: 0.7f), Atom(3, " N", "ALA", 1, 2, element: "N"));
    Check(alt.Atoms.Count == 2 && alt.Atoms.Any(a => a.Serial == 2) && !alt.Atoms.Any(a => a.Serial == 1), "One alternate plus shared atoms");
    var models = Parse("MODEL        1", Atom(1, " C1", "LIG", 1, 0), Atom(2, " C2", "LIG", 1, 1.5f),
      "ENDMDL", "MODEL        2", Atom(1, " C1", "LIG", 1, 20), "ENDMDL", "CONECT    1    2", "CONECT    2    1");
    Check(models.Atoms.Count == 2 && models.Atoms[0].X == 0, "Only first model");
    Check(models.Bonds.Count == 1, "Global CONECT after multiple models and bond deduplication");
    var unknown = Parse(Atom(1, " C1", "LIG", 1, 0), Atom(2, " C2", "LIG", 1, 1.5f));
    Check(unknown.Bonds.Count == 0 && unknown.Warnings.Count > 0, "Unknown ligand topology not invented");
    var element = Parse(Atom(1, " CA", "ALA", 1, 0, element: ""), Atom(2, "CA", "CAL", 2, 5, element: ""));
    Check(element.Atoms[0].Element == "C" && element.Atoms[1].Element == "CA", "Carbon alpha versus calcium");
    var water = Parse(Atom(1, " CA", "ALA", 1, 0), Atom(2, " O", "HOH", 2, 5, element: "O"));
    Check(water.Atoms.Count == 1, "Water omitted");
    var hydrogen = Parse(Atom(1, " N", "ALA", 1, 0, element: "N"), Atom(2, " H", "ALA", 1, 1, element: "H"));
    Check(hydrogen.Bonds.Count == 1, "Present hydrogen attachment");
    Reject(""); Reject("ATOM      1");
    Reject(Atom(1, " CA", "ALA", 1, float.NaN));
    Reject(string.Join("\n", Atom(1, " CA", "ALA", 1, 0), Atom(1, " N", "ALA", 1, 1)));
    Reject(new string('x', PdbStructure.MaximumTextLength + 1));
    string fixture = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "Fixtures", "4INS.pdb");
    if (File.Exists(fixture))
    {
      string source = File.ReadAllText(fixture);
      var insulin = PdbStructure.Parse(source);
      Check(insulin.Atoms.Count > 100 && insulin.Bonds.Count > 100, "Real insulin structure parsed");
      int disulfides = 0;
      foreach (string line in source.Split('\n').Where(l => l.StartsWith("SSBOND")))
      {
        string firstChain = line.Substring(15, 1), firstResidue = line.Substring(17, 5).Trim();
        string secondChain = line.Substring(29, 1), secondResidue = line.Substring(31, 5).Trim();
        var first = insulin.Atoms.Single(a => a.Chain == firstChain && a.ResidueId == firstResidue && a.Name == "SG");
        var second = insulin.Atoms.Single(a => a.Chain == secondChain && a.ResidueId == secondResidue && a.Name == "SG");
        Check(Bond(insulin, first.Serial, second.Serial), "Disulfide matches independent SSBOND annotation");
        disulfides++;
      }
      Check(disulfides > 0, "Insulin fixture contains annotated disulfides");
      Console.WriteLine($"Insulin fixture: {insulin.Atoms.Count} atoms, {insulin.Bonds.Count} bonds");
    }
    else throw new Exception("Insulin fixture missing: " + fixture);
    Console.WriteLine($"Passed {checks} PDB checks.");
  }
}
