namespace RayTracer.Helpers
open RayTracer.Expressions.ExprToPoly
open RayTracer.Entities.Point
open RayTracer.Expressions.ExprParse

module EquationHelpers = 

    val solveSecondDegree : float -> float -> float -> (float*float) option
    
    val posMin : float -> float -> float option

    val degreeOf : simpleExpr -> int

    val solveFirstDegree : float -> float -> float option

    val solveExprForPoint : simpleExpr -> Point -> float

    val Collectstring : string -> float

    exception NotImplemented

    val getDerivative : Map<int,float> -> Map<int,float>

    val sortExpr : simpleExpr -> simpleExpr

    val divide : (float * string * int) -> (float * string * int) -> (float * string * int)

    val simpleExprToListList : simpleExpr -> atomGroup list

    val join : Map<'a,'b> -> Map<'a,'b> -> Map<'a,'b>

    val buildMissingExp : int -> int -> Map<int,float>

    val fillPoly : Map<int,float> -> Map<int,float>

    val cleanPoly : Map<int,float> -> Map<int,float>

    val getDegree : Map<int,_> -> int

    val atomTimesPoly : (float * string * int) -> Map<int,float> -> Map<int,float>

    val polySubtract : Map<int,float> -> Map<int,float> -> Map<int,float>

    val polyAdd : Map<int,float> -> Map<int,float> -> Map<int,float>

    val polyLongDiv : Map<int,float> -> Map<int,float> -> string ->  Map<int,float>

    exception DivideByEmptyPolyException

    val solvePoly : Map<int,float> -> float -> float

    val getSturmSequence : Map<int,float> -> string -> float -> float list

    val signChanges : float list -> float

    val diff : float -> float -> float

    val helpFinder : Map<int,float> -> float -> float -> float -> float -> int -> float

    val findRoot : Map<int,float> -> float option

    val findTbox : float -> float -> float -> float -> (float*float)

    val deriveForVar : simpleExpr -> string -> simpleExpr

    val newton_raphson : Map<int,float> -> float -> float