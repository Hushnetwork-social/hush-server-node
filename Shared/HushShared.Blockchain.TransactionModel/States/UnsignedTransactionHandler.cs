using System.Text.Json;
using HushShared.Blockchain.Model;

namespace HushShared.Blockchain.TransactionModel.States;

public static class UnsignedTransactionHandler
{
    public static UnsignedTransaction<T> CreateNew<T>(Guid payloadType, Timestamp CreationTimeStamp, T payload)
        where T : ITransactionPayloadKind => 
        new(
            TransactionId.NewTransactionId,
            payloadType,
            CreationTimeStamp,
            payload,
            GetObjectSize(payload));


    private static long GetObjectSize<T>(T obj)
    {
        if (obj == null) return 0;

        try
        {
            var options = new JsonSerializerOptions();
            using MemoryStream stream = new();
            JsonSerializer.Serialize(stream, obj, options);
            return stream.Length;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error serializing object: {ex.Message}");
            return 0; // Or throw the exception if you prefer
        }
    }
}
