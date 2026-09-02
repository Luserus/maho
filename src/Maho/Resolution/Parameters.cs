using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal record struct Parameters(IReadOnlyList<TypeSyntax> Types);