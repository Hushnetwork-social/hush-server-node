// using System.Text.Json;
// using System.Text.Json.Serialization.Metadata;
// using HushEcosystem.Model;
// using HushEcosystem.Model.Blockchain;
// using Olimpo;

// namespace HushServerNode.Blockchain;

// public static class TransactionBaseExtensions
// {
//     public static void Sign(this ISignable signableObject, string privateKey, TransactionBaseConverter transactionBaseConverter)
//     {
//         var jsonOptions = new JsonSerializerOptions
//         {
//             TypeInfoResolver = new DefaultJsonTypeInfoResolver
//             {
//                 Modifiers = { ExcludeSignatureProperty }
//             },
//             Converters = { transactionBaseConverter }
//         };

//         var json = signableObject.ToJson(jsonOptions);

//         signableObject.Signature = SigningKeys.SignMessage(json, privateKey);
//     }

//     public static void HashObject(this IHashable hashableObject, TransactionBaseConverter transactionBaseConverter)
//     {
//         var jsonOptions = new JsonSerializerOptions
//         {
//             TypeInfoResolver = new DefaultJsonTypeInfoResolver
//             {
//                 Modifiers = { ExcludeHashProperty, ExcludeSignatureProperty }
//             },
//             Converters = { transactionBaseConverter }
//         };

//         var json = hashableObject.ToJson(jsonOptions);
//         hashableObject.Hash = json.GetHashCode().ToString();
//     }

//     public static bool CheckSignature(this ISignable signableObject, string signingAddress, TransactionBaseConverter transactionBaseConverter)
//     {
//         var jsonOptions = new JsonSerializerOptions
//         {
//             TypeInfoResolver = new DefaultJsonTypeInfoResolver
//             {
//                 Modifiers = { ExcludeSignatureProperty }
//             },
//             Converters = { transactionBaseConverter }
//         };

//         var json = signableObject.ToJson(jsonOptions);
//         return SigningKeys.VerifySignature(json, signableObject.Signature, signingAddress);
//     }

//     static void ExcludeSignatureProperty(JsonTypeInfo jsonTypeInfo)
//     {
//         if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
//             return;

//         foreach (JsonPropertyInfo property in jsonTypeInfo.Properties)
//         {
//             if (property.Name.Contains("Signature"))
//             {
//                 property.Get = null;
//                 property.Set = null;
//             }
//         }
//     }

//     static void ExcludeHashProperty(JsonTypeInfo jsonTypeInfo)
//     {
//         if (jsonTypeInfo.Kind != JsonTypeInfoKind.Object)
//             return;

//         foreach (JsonPropertyInfo property in jsonTypeInfo.Properties)
//         {
//             if (property.Name.Contains("Hash"))
//             {
//                 property.Get = null;
//                 property.Set = null;
//             }
//         }
//     }
// }
