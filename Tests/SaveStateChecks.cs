using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

// Run under Unity's bundled Mono against the compiled project assembly.
// Exercises the actual NodeState type without starting the editor or touching saves.
class SaveStateChecks
{
  sealed class LegacyFields : ISerializationSurrogate
  {
    public void GetObjectData(object value, SerializationInfo info, StreamingContext context)
    {
      foreach (FieldInfo field in value.GetType().GetFields())
        if (!field.IsDefined(typeof(OptionalFieldAttribute), false))
          info.AddValue(field.Name, field.GetValue(value), field.FieldType);
    }
    public object SetObjectData(object value, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
    { throw new NotSupportedException(); }
  }
  static int checks;
  static void Check(bool condition, string message)
  { if (!condition) throw new Exception(message); checks++; }
  static object RoundTrip(object state, bool legacy = false)
  {
    var writer = new BinaryFormatter();
    if (legacy)
    {
      var selector = new SurrogateSelector();
      selector.AddSurrogate(state.GetType(), new StreamingContext(StreamingContextStates.All), new LegacyFields());
      writer.SurrogateSelector = selector;
    }
    using (var stream = new MemoryStream())
    {
      writer.Serialize(stream, state); stream.Position = 0;
      return new BinaryFormatter().Deserialize(stream);
    }
  }
  static void Main(string[] args)
  {
    AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
      string name = new AssemblyName(request.Name).Name + ".dll";
      foreach (string directory in new[] { Path.GetDirectoryName(args[0]), args[1] })
      {
        string file = Path.Combine(directory, name);
        if (File.Exists(file)) return Assembly.LoadFrom(file);
      }
      return null;
    };
    Type type = Assembly.LoadFrom(args[0]).GetType("ApplicationState+NodeState", true);
    for (int mode = 0; mode <= 4; mode++)
    for (int visible = 1; visible <= 2; visible++)
    {
      object source = FormatterServices.GetUninitializedObject(type);
      type.GetField("pdbRepresentation").SetValue(source, mode);
      type.GetField("mediaDisplayOverride").SetValue(source, visible);
      type.GetField("modelUri").SetValue(source, "https://example.org/protein.pdb");
      type.GetField("modelCandidates").SetValue(source, new System.Collections.Generic.List<string> { "https://example.org/protein.pdb" });
      type.GetField("modelLocalTransform").SetValue(source, new float[] { .125f, .125f, .125f, -1f, 2f, 3f });
      type.GetField("modelDisplaySize").SetValue(source, 2.5f);
      object loaded = RoundTrip(source);
      Check((int)type.GetField("pdbRepresentation").GetValue(loaded) == mode, "PDB mode round trip");
      Check((int)type.GetField("mediaDisplayOverride").GetValue(loaded) == visible, "Visibility round trip");
      float[] transform = (float[])type.GetField("modelLocalTransform").GetValue(loaded);
      Check(transform.Length == 6 && transform[0] == .125f && transform[3] == -1f && transform[5] == 3f, "Exact local scale and offset round trip");
      Check(((System.Collections.Generic.List<string>)type.GetField("modelCandidates").GetValue(loaded))[0] == "https://example.org/protein.pdb", "URL round trip");
      if (mode == 0 && visible == 1)
      {
        object old = RoundTrip(source, true);
        Check((int)type.GetField("pdbRepresentation").GetValue(old) == 0, "Old saves default to Bonds");
        Check(type.GetField("modelLocalTransform").GetValue(old) == null, "Old saves omit exact transform safely");
        Check((float)type.GetField("modelDisplaySize").GetValue(old) == 2.5f, "Old model size retained");
        Check((int)type.GetField("mediaDisplayOverride").GetValue(old) == 0, "Old saves keep creation default for visibility");
      }
    }
    Console.WriteLine("Passed " + checks + " actual NodeState serialization checks.");
  }
}
