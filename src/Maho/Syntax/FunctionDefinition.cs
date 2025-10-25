using System;

namespace Maho.Syntax;

internal sealed class FunctionDefinition : FunctionMember
{
    public BlockStatement Body { get; }

    public FunctionDefinition(FunctionSignature signature, BlockStatement body) : base(signature)
    {
        Body = body;
    }
}