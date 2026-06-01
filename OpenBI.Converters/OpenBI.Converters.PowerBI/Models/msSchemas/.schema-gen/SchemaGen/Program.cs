using System.Text.Json;
using System.Text.RegularExpressions;
using NJsonSchema;
using NJsonSchema.CodeGeneration.CSharp;

var schemasRoot = Directory.GetCurrentDirectory();
var modelsRoot = Path.Combine(schemasRoot, "Models");
Directory.CreateDirectory(modelsRoot);

var schemaFiles = Directory
    .GetFiles(schemasRoot, "*_schema.json", SearchOption.TopDirectoryOnly)
    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
    .ToList();

foreach (var schemaFile in schemaFiles)
{
    await using var stream = File.OpenRead(schemaFile);
    using var doc = await JsonDocument.ParseAsync(stream);
    var schemaId = doc.RootElement.GetProperty("$id").GetString()!;
    var segments = schemaId.Split('/', StringSplitOptions.RemoveEmptyEntries);
    var objectName = segments[^3];
    var version = segments[^2];

    var schemaUrl = $"https://raw.githubusercontent.com/microsoft/json-schemas/main/fabric/item/report/definition/{objectName}/{version}/schema.json";
    var schemaEmbeddedUrl = $"https://raw.githubusercontent.com/microsoft/json-schemas/main/fabric/item/report/definition/{objectName}/{version}/schema-embedded.json";

    JsonSchema schema;
    try
    {
        schema = await JsonSchema.FromUrlAsync(schemaUrl);
    }
    catch
    {
        schema = await JsonSchema.FromUrlAsync(schemaEmbeddedUrl);
    }

    var objectPascal = ToPascalCase(objectName);
    var namespaceName = $"OpenBI.Converters.PowerBI.Models.msSchemas.Models.{objectPascal}";
    var settings = new CSharpGeneratorSettings
    {
        Namespace = namespaceName,
        ClassStyle = CSharpClassStyle.Poco,
        JsonLibrary = CSharpJsonLibrary.NewtonsoftJson,
        GenerateNullableReferenceTypes = true,
        GenerateDataAnnotations = false,
        GenerateDefaultValues = false
    };

    var generator = new CSharpGenerator(schema, settings);
    var className = objectPascal + "Root";
    var code = generator.GenerateFile(className);

    code = EnsurePlaceholderType(code, "Target");
    code = EnsurePlaceholderType(code, "Expression");
    code = EnsurePlaceholderType(code, "Field");
    code = EnsurePlaceholderType(code, "FieldExpr");

    var objectDir = Path.Combine(modelsRoot, objectPascal);
    Directory.CreateDirectory(objectDir);
    var outFile = Path.Combine(objectDir, objectPascal + "Models.cs");
    await File.WriteAllTextAsync(outFile, code);
    Console.WriteLine($"Generated {objectPascal} -> {outFile}");
}

Console.WriteLine($"Processed {schemaFiles.Count} schema files.");

static string EnsurePlaceholderType(string code, string typeName)
{
    if (!Regex.IsMatch(code, $@"\b{typeName}\b", RegexOptions.CultureInvariant) ||
        code.Contains($"class {typeName}", StringComparison.Ordinal))
    {
        return code;
    }

    var marker = "#pragma warning disable // Disable all warnings";
    var placeholderClass = $@"

    [System.CodeDom.Compiler.GeneratedCode(""NJsonSchema"", ""11.5.2.0 (Newtonsoft.Json v13.0.0.0)"")]
    public partial class {typeName}
    {{
        [Newtonsoft.Json.JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, object?> AdditionalProperties {{ get; set; }} =
            new System.Collections.Generic.Dictionary<string, object?>();
    }}
";
    return code.Replace(marker, marker + placeholderClass, StringComparison.Ordinal);
}

static string ToPascalCase(string input)
{
    var parts = input
        .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
    return string.Concat(parts);
}
