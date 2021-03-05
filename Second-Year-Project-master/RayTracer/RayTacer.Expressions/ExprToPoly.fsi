namespace RayTracer.Expressions
module ExprToPoly =

    type expr = ExprParse.expr
    val ppExpr : expr -> string
    val subst: expr -> (string * expr) -> expr

    type atom = ANum of float | AExponent of string * int | ARoot of (atom list list) * int
    type atomGroup = atom list  
    type simpleExpr = SE of atomGroup list

    val ppSimpleExpr: simpleExpr -> string
    val exprToSimpleExpr: expr -> simpleExpr
    val divExprToBigDiv : expr -> expr
    val simplifyRootsinAG : atomGroup -> bool -> atom list list
    val addSimRootToAG : atom list list list -> simpleExpr
    val removeRoots : simpleExpr -> simpleExpr * simpleExpr
    val resultOfRemoveRoots : simpleExpr * simpleExpr -> simpleExpr
    val subSimple : atomGroup list -> Map<string,float> -> Map<int,float>
    val simpleExprToExpr : simpleExpr -> expr

    val simplify: expr -> atom list list
    val simplifyAtomGroup : atom list -> atom list
    val simplifySimpleExpr : simpleExpr -> simpleExpr