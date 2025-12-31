namespace HushShared.Blockchain.TransactionModel;

public class TransactionDeserializerHandler
{
    private static TransactionDeserializerHandler? _instance;

    public readonly IEnumerable<ITransactionDeserializerStrategy> SpecificDeserializers = [];

    public static TransactionDeserializerHandler Instance
    {
        get
        {
            return _instance ?? throw new InvalidOperationException("TransactionDeserializerHandler has not been initialized.");
        }
    }

    public TransactionDeserializerHandler(IEnumerable<ITransactionDeserializerStrategy> specificDeserializers)
    {
        this.SpecificDeserializers = specificDeserializers;

        _instance = this;
    }
}
