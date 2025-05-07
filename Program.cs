using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using System.Data.SQLite;

namespace xBIMProject
{
    class Program
    {
        static void Main(string[] args)
        {
            string ifcFilePath = @"C:\Users\ADMIN\Downloads\Snowdon Towers Sample Architectural\Snowdon Towers Sample Architectural.ifc";

            try
            {
                using (var model = IfcStore.Open(ifcFilePath))
                {
                    Console.WriteLine("Fichier IFC chargé avec succès.\n");
                    Console.WriteLine($"Modèle chargé : {ifcFilePath}");
                    Console.WriteLine(new string('-', 100));

                    // Dictionnaire pour regrouper toutes les entités par type
                    var entitiesByType = new Dictionary<string, List<object[]>>();

                    var rootEntities = model.Instances.OfType<IIfcRoot>();
                    foreach (var entity in rootEntities)
                    {
                        string type = entity.GetType().Name;
                        string simplifiedType = type.StartsWith("Ifc") ? type.Substring(3) : type;

                        if (!entitiesByType.ContainsKey(simplifiedType))
                        {
                            entitiesByType[simplifiedType] = new List<object[]>();
                        }

                        var rowData = GetEntityData(entity, simplifiedType);
                        entitiesByType[simplifiedType].Add(rowData);
                    }

                    // Export vers SQLite
                    // Export vers SQLite
string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
string dbFilePath = Path.Combine(desktopPath, $"IFC_Export_{DateTime.Now:yyyyMMdd_HHmmss}.db");

using (var connection = new SQLiteConnection($"Data Source={dbFilePath};Version=3;"))
{
    connection.Open();

    // Création des tables spécifiques pour chaque type
    foreach (var type in entitiesByType.Keys.OrderBy(t => t))
    {
        string safeTableName = type.Replace(" ", "_"); // Remplace les espaces si présents
        string createTableQuery = $@"
            CREATE TABLE IF NOT EXISTS ""{safeTableName}"" (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT,
                GlobalId TEXT,
                Description TEXT,
                Length TEXT,
                Width TEXT,
                Height TEXT,
                Position TEXT,
                Material TEXT,
                StyleName TEXT,
                RelatedObjects TEXT,
                IsExternal TEXT
            )";
        using (var command = new SQLiteCommand(createTableQuery, connection))
        {
            command.ExecuteNonQuery();
        }

        // Insertion des données dans la table correspondante
        string insertQuery = $@"
            INSERT INTO ""{safeTableName}"" (
                Name, GlobalId, Description, Length, Width, Height, Position,
                Material, StyleName, RelatedObjects, IsExternal
            ) VALUES (
                @Name, @GlobalId, @Description, @Length, @Width, @Height, @Position,
                @Material, @StyleName, @RelatedObjects, @IsExternal
            )";

        foreach (var entityData in entitiesByType[type])
        {
            using (var command = new SQLiteCommand(insertQuery, connection))
            {
                command.Parameters.AddWithValue("@Name", entityData[1]);
                command.Parameters.AddWithValue("@GlobalId", entityData[2]);
                command.Parameters.AddWithValue("@Description", entityData[3]);
                command.Parameters.AddWithValue("@Length", entityData[4]);
                command.Parameters.AddWithValue("@Width", entityData[5]);
                command.Parameters.AddWithValue("@Height", entityData[6]);
                command.Parameters.AddWithValue("@Position", entityData[7]);
                command.Parameters.AddWithValue("@Material", entityData[8]);
                command.Parameters.AddWithValue("@StyleName", entityData[9]);
                command.Parameters.AddWithValue("@RelatedObjects", entityData[10]);
                command.Parameters.AddWithValue("@IsExternal", entityData[11]);
                command.ExecuteNonQuery();
            }
        }
    }

    connection.Close();
    Console.WriteLine($"Base de données SQLite exportée : {dbFilePath}");
    Console.WriteLine($"Nombre total d'entités : {rootEntities.Count()}");
    Console.WriteLine($"Types de tables créées : {string.Join(", ", entitiesByType.Keys.OrderBy(t => t))}");
}
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur : {ex.Message}");
                Console.WriteLine($"Détails : {ex.StackTrace}");
            }
        }

        // Fonction pour extraire les données d'une entité (inchangée)
        static object[] GetEntityData(IIfcRoot entity, string simplifiedType)
        {
            string name = entity.Name?.ToString() ?? "Sans nom";
            string globalId = entity.GlobalId.ToString() ?? "";
            string description = entity.Description?.ToString() ?? simplifiedType;

            string length = "";
            string width = "";
            string height = "";
            string position = "";
            string material = "";
            string styleName = "";
            string relatedObjects = "";
            string isExternal = "";

            if (entity is IIfcProduct product && product.ObjectPlacement != null)
            {
                if (product.ObjectPlacement is IIfcLocalPlacement placement)
                {
                    var transform = placement.RelativePlacement as IIfcAxis2Placement3D;
                    if (transform?.Location != null)
                    {
                        position = $"({transform.Location.X}, {transform.Location.Y}, {transform.Location.Z})";
                    }
                }
            }

            if (entity is IIfcElement element)
            {
                var bbox = element.Representation?.Representations
                    .SelectMany(r => r.Items)
                    .OfType<IIfcBoundingBox>()
                    .FirstOrDefault();

                if (bbox != null)
                {
                    length = bbox.XDim.ToString();
                    width = bbox.YDim.ToString();
                    height = bbox.ZDim.ToString();
                }
                else
                {
                    foreach (var representation in element.Representation?.Representations ?? Enumerable.Empty<IIfcRepresentation>())
                    {
                        foreach (var item in representation.Items)
                        {
                            if (item is IIfcExtrudedAreaSolid extruded)
                            {
                                if (extruded.SweptArea is IIfcRectangleProfileDef rectProfile)
                                {
                                    length = rectProfile.XDim.ToString();
                                    width = rectProfile.YDim.ToString();
                                    height = extruded.Depth.ToString();
                                }
                            }
                        }
                    }
                }
            }

            if (entity is IIfcObject obj)
            {
                var materials = obj.HasAssociations
                    .OfType<IIfcRelAssociatesMaterial>()
                    .Select(r => r.RelatingMaterial)
                    .Select(m => m switch
                    {
                        IIfcMaterial mat => mat.Name.ToString(),
                        IIfcMaterialList matList => string.Join(", ", matList.Materials.Select(m => m.Name.ToString())),
                        IIfcMaterialLayerSet matLayerSet => string.Join(", ", matLayerSet.MaterialLayers.Select(l => l.Material?.Name.ToString())),
                        IIfcMaterialLayerSetUsage matLayerSetUsage => string.Join(", ", matLayerSetUsage.ForLayerSet.MaterialLayers.Select(l => l.Material?.Name.ToString())),
                        _ => ""
                    })
                    .Where(m => !string.IsNullOrEmpty(m))
                    .Distinct();
                material = materials.Any() ? string.Join("; ", materials) : "";

                if (obj is IIfcProduct productWithRep)
                {
                    var styles = productWithRep.Representation?.Representations
                        .SelectMany(r => r.Items)
                        .SelectMany(i => i.Model.Instances.Where<IIfcStyledItem>(si => si.Item == i))
                        .SelectMany(si => si.Styles.OfType<IIfcPresentationStyle>())
                        .Select(s => s.Name?.ToString() ?? "Unnamed Style")
                        .Distinct();
                    styleName = styles != null && styles.Any() ? string.Join("; ", styles) : "";
                }

                var relObjects = obj.HasAssociations
                    .OfType<IIfcRelAssociates>()
                    .SelectMany(ra => ra.RelatedObjects.OfType<IIfcRoot>())
                    .Select(ro => ro.Name?.ToString() ?? ro.GlobalId.ToString() ?? "")
                    .Distinct();
                relatedObjects = relObjects.Any() ? string.Join("; ", relObjects) : "";

                var propertySets = obj.IsDefinedBy
                    .Select(r => r.RelatingPropertyDefinition)
                    .OfType<IIfcPropertySet>()
                    .SelectMany(ps => ps.HasProperties)
                    .OfType<IIfcPropertySingleValue>();

                isExternal = propertySets
                    .Where(p => p.Name.ToString().ToLower() == "isexternal")
                    .Select(p => p.NominalValue?.ToString() ?? "")
                    .FirstOrDefault() ?? "";
            }

            return new object[] {
                simplifiedType, name, globalId, description, length, width, height, position,
                material, styleName, relatedObjects, isExternal
            };
        }
    }
}