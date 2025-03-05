using HushNode.Interfaces;

namespace HushNode.InternalModules.Bank.Model;

public record AddressBalance(
    string PublicAddress,
    string Token,
    string Balance)
{
    // public static AddressBalance NoBalance(string publicAddress, string token) =>  
    //     new(publicAddress, token, DecimalStringConverter.DecimalToString(0m));
}

public record AddressNoBalance : AddressBalance
{
    public AddressNoBalance(
        string PublicAddress, 
        string Token) : 
        base(PublicAddress, Token, DecimalStringConverter.DecimalToString(0m))
    {
    }
}