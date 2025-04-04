using HushShared.Converters;

namespace HushNode.Bank.Model;

public record AddressBalance(
    string PublicAddress,
    string Token,
    string Balance);

public record AddressNoBalance : AddressBalance
{
    public AddressNoBalance(
        string PublicAddress, 
        string Token) : 
        base(PublicAddress, Token, DecimalStringConverter.DecimalToString(0m))
    {
    }
}