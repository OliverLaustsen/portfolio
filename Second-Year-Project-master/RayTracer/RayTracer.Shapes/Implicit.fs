namespace RayTracer.Shapes
open RayTracer.Expressions.ExprParse
open RayTracer.Expressions.ExprToPoly
open RayTracer.Helpers.EquationHelpers
open RayTracer.Entities.Ray
open RayTracer.Entities
open Vector
open BoundingBox
open BaseShape
open Point
open Hit

module Implicit = 

    let insideImp expr p = 
        let res = solveExprForPoint expr p
        if res > 0.0 then false
        elif res < 0.0 then true
        else false

    let solvePolyForRay ray degree agl =  
        let o = Ray.getPoint ray
        let ox = Point.getX o
        let oy = Point.getY o 
        let oz = Point.getZ o

        let d = Ray.getVector ray
        let dx = Vector.getX d
        let dy = Vector.getY d
        let dz = Vector.getZ d
        

        let replaceMap = Map ([("ox", ox); ("dx", dx); ("oy", oy); ("dy", dy); ("oz", oz); ("dz", dz)])
        let subsexpr = subSimple agl replaceMap

        if degree = 1 then 
            let first = Map.find 1 subsexpr
            let zeroth = Map.find 0 subsexpr
            let zero = if zeroth < 0.000001 && zeroth > -0.000001 then 0.0 else zeroth
            let root = solveFirstDegree zeroth first       
            root  
        else if degree = 2 then 
            let second = Map.find 2 subsexpr
            let first = Map.find 1 subsexpr
            let zeroth = Map.find 0 subsexpr
            let zero = if zeroth < 0.000001 && zeroth > -0.000001 then 0.0 else zeroth
            let res = solveSecondDegree second first zero
            match res with 
                | Some(x1,x2) -> posMin x1 x2
                | None -> None
        else 
            if degree > 2 then 
                findRoot subsexpr
            else 
                None

    let mkImplicit (e:string) =
        let se = [for a in e -> a]
        let exp = parseStr se
        let exp2 = divExprToBigDiv exp

        let exp3 = 
                match exp2 with
                | FDiv (e,_) -> e
                | _ -> failwith "Expected an FDiv"

        let (SE expsim) = exprToSimpleExpr exp3

        let expr2000 = simpleExprToExpr (SE expsim)
//        printfn "%A" expr2000
        
        let degree = degreeOf (SE expsim)

        let ex = FAdd (FVar "ox", FMult (FVar "t", FVar "dx"))
        let ey = FAdd (FVar "oy", FMult (FVar "t", FVar "dy"))
        let ez = FAdd (FVar "oz", FMult (FVar "t", FVar "dz"))
        let exps = List.fold subst expr2000 [("x", ex); ("y", ey); ("z", ez)]

        let derivX = 
            let (SE unfiltered) = (deriveForVar (SE expsim) "x")
            SE (List.fold (fun acc ag -> if List.length ag > 0 then ag::acc else acc) List.Empty unfiltered)
        let derivY =             
            let (SE unfiltered) = (deriveForVar (SE expsim) "y")
            SE (List.fold (fun acc ag -> if List.length ag > 0 then ag::acc else acc) List.Empty unfiltered)
        let derivZ =             
            let (SE unfiltered) = (deriveForVar (SE expsim) "z")
            SE (List.fold (fun acc ag -> if List.length ag > 0 then ag::acc else acc) List.Empty unfiltered)
        

        let (SE simplExpr) = exprToSimpleExpr exps
        let agl = List.map simplifyAtomGroup simplExpr
        {   new BaseShape with 
                member this.BoundingBox = mkBoundingBox (mkPoint -1000.0 -1000.0 -1000.0) (mkPoint 1000.0 1000.0 1000.0)
                member this.TransformedBoundingBox = this.BoundingBox
                member this.Inside = Some (insideImp (SE expsim))
                member this.Hit = fun ray texture ->
                    let t = solvePolyForRay ray degree agl
                    match t with 
                        | Some(t) -> 
                            if t > 0.0 then
                                let hitPoint = Ray.getPosition ray t
                                let x = solveExprForPoint derivX hitPoint
                                let y = solveExprForPoint derivY hitPoint
                                let z = solveExprForPoint derivZ hitPoint
                                let pointAsVector = Vector.mkVector (x) (y) (z)
                                let normal = Vector.normalise pointAsVector
                            
                                let material = Texture.getMaterial texture 0.0 0.0

                                //Change when implementing texture
                                Some(mkHit t normal material)
                            else None
                        | _ -> None

        }    

    let deriveForVar expr s = 
        match expr with 
        | SE(se) -> let mutable deriv = []
                    for group in se do
                        let mutable prev = false
                        let mutable prevVal = 1.0
                        let mutable derivAG = List.Empty
                        let mutable prevList = []
                        for atom in group do 
                            match atom with
                            | ANum x -> 
                                    prev <- true
                                    prevVal <- x 
                            | AExponent(a,exp) ->             
                                    if s = a then
                                        let newPWR = (float exp)*prevVal
                                        derivAG <- (ANum newPWR)::(AExponent (a,(exp-1)))::prevList
                                    else
                                        prevList <- (ANum prevVal)::(AExponent (a,(exp)))::prevList
                                        if List.length derivAG <> 0 then derivAG <- derivAG @ prevList
                            | ARoot(_,_) -> failwith "There should be no roots"
                        deriv <- derivAG::deriv 
                    SE deriv                          