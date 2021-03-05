namespace RayTracer.Helpers
open RayTracer.Expressions.ExprToPoly
open RayTracer.Entities.Point

module EquationHelpers = 

    (*Quadractic formula*)
    let D a b c =  (b**2.0) - (4.0 * a * c)
    let Ep a b d = (-b + sqrt(d))/(2.0*a)
    let Em a b d = (-b - sqrt(d))/(2.0*a)

    (*Check if number is positive return some*)
    let pos n = if n > 0.0 then Some n else None

    (* returns the smallest positive number*)
    let posMin n m = 
        match pos n with
         None -> pos m
        |Some vn -> 
            match pos m with
              None -> Some vn
            | Some vm -> Some(min vn vm)

    (*Returns smallest positive solution*)
    let solveSecondDegree a b c = 
        if a = 0.0 then None
        else 
            let d = D a b c
            if d < 0.000000000000001 then None
            else 
                let v = Ep a b d
                let v1 = Em a b d 
                Some(v,v1)

    let degreeOf (e:simpleExpr) = 
        match e with 
        SE(se) -> let mutable cum = ref 0;
                  for group in se do
                    for atom in group do 
                        match atom with
                            AExponent(a,exp) -> if exp > cum.Value then cum := exp
                            | s -> cum := cum.Value
                  cum.Value

    let solveFirstDegree (b:float) (a:float) =
        if b >= 0.0 then
            Some((-b)/a)
            else None
    
    let rec solveExprForPoint (SE expr) point = 
        let x = RayTracer.Entities.Point.getX point
        let y = RayTracer.Entities.Point.getY point
        let z = RayTracer.Entities.Point.getZ point
        List.fold (fun sum ag ->
            let valueOfAG =
                List.fold (fun sumOfAG atom ->
                    match atom with
                    | ANum n -> sumOfAG * n
                    | AExponent(v,n) -> 
                        if v = "x" then sumOfAG * (pown x n)
                        elif v = "y" then sumOfAG * (pown y n)
                        elif v = "z" then sumOfAG * (pown z n)
                        else failwith "Unknown AExponent"
                    | ARoot (_,_) -> failwith "Unexpected Root"
                ) 1.0 ag
            sum + valueOfAG
        ) 0.0 expr

//    let rec solveExprForPoint expr point = 
//        match expr with 
//        | SE(se) -> 
////        let mutable result = 
////                        match se with
////                        | [[];[]] -> 0.0
////                        | _ -> 1.0
//                    let mutable totalResult = 0.0
//                    let x = RayTracer.Entities.Point.getX point
//                    let y = RayTracer.Entities.Point.getY point
//                    let z = RayTracer.Entities.Point.getZ point
//                    for group in se do
////                        let mutable agResult = 0.0
////                        match group with
////                            | [] -> result <- 0.0
////                            | _ -> result <- 1.0
//                        let mutable prevVal = 1.0
//                        for atom in group do
//                            match atom with
//                            | ANum n -> prevVal <- n * prevVal
//                            | AExponent (a,exp) -> 
//                                                 match a with
////                                                 | ax when a = "x" -> result <- result * (prevVal * (pown x exp))
//                                                 | ax when a = "x" -> prevVal <- (prevVal * (pown x exp))
////                                                 | ay when a = "y" -> result <- result * (prevVal * (pown y exp))
//                                                 | ay when a = "y" -> prevVal <- (prevVal * (pown y exp))
////                                                 | az when a = "z" -> result <- result * (prevVal * (pown z exp))
//                                                 | az when a = "z" -> prevVal <- (prevVal * (pown z exp))
//                                                 | _ -> ()
//                            | ARoot(_,_) -> failwith "There should be no roots"
//                        if not (prevVal = 1.0) then totalResult <- totalResult + prevVal
//                    totalResult

    let Collectstring (s: string) = 
        let first = s.Split '+'
        let mutable elements = []
        let x = first.Length
        let mutable f = true
        for c in first do
            if f then 
                f<- false
                let e = float (System.Single.Parse c)
                elements <- e::elements 
            else
                let e = float (System.Single.Parse c)
                elements <- elements@[e]
        let d = List.fold (fun x y -> x + y) 0.0 elements
        d

    exception NotImplemented
    
    (* Return the derivative of a poly *)
    let getDerivative p =
        Map.fold(fun derivOfP exp f ->
                  if not (exp = 0) then
                      let derivedf = f * (float exp)
                      Map.add (exp-1) derivedf derivOfP
                  else derivOfP) Map.empty p

    (* Return the given expression in sorted by the power of a variable *)
    let sortExpr e : simpleExpr =
        match e with 
            SE(se) -> let mutable sortedExpr = List.Empty
                      for group in se do
                        let mutable sortedAG = List.Empty;
                        let mutable prev = false
                        let mutable prevVal = 1.0
                        for atom in group do
                            match atom with
                                | ANum n -> if sortedAG.Length = 0 then
                                                sortedAG <- (ANum n)::sortedAG
                                            else 
                                                prev <- true
                                                prevVal <- n
                                | AExponent (x,pwr) -> if sortedAG.Length = 0 then
                                                            sortedAG <- (AExponent (x,pwr))::sortedAG
                                                       else 
                                                            let compare = sortedAG.Head
                                                            match compare with
                                                                | ANum n                -> sortedAG <- (AExponent (x,pwr))::sortedAG
                                                                | AExponent (y,pwr2)    -> if pwr2 > pwr then
                                                                                                if prev then sortedAG <- (ANum prevVal)::sortedAG
                                                                                                sortedAG <- (AExponent (x,pwr))::sortedAG
                                                                                           else 
                                                                                                if prev then sortedAG <- sortedAG@[(ANum prevVal)]
                                                                                                sortedAG <- sortedAG@[(AExponent (x,pwr))]
                                                                | ARoot(_,_) -> failwith "There should be no roots"
                                | ARoot(_,_) -> failwith "There should be no roots"
                        sortedAG <- List.rev sortedAG
                        if sortedExpr.Length = 0 then
                            sortedExpr <- sortedAG::sortedExpr
                        else
                            let compare = sortedExpr.Head.Head
                            let compareTO = sortedAG.Head
                            match compare with
                                | ANum n            -> match compareTO with
                                                        | ANum n2               -> if n > n2 then
                                                                                        sortedExpr <- sortedAG::sortedExpr
                                                                                    else
                                                                                        sortedExpr <- sortedExpr@[sortedAG]
                                                        | AExponent (y,pwr2)    -> if n > (float pwr2) then
                                                                                        sortedExpr <- sortedAG::sortedExpr
                                                                                   else
                                                                                        sortedExpr <- sortedExpr@[sortedAG]
                                                        | ARoot(_,_) -> failwith "There should be no roots"
                                | AExponent (x,pwr) -> match compareTO with
                                                        | ANum n2               -> if (float pwr) > n2 then
                                                                                        sortedExpr <- sortedAG::sortedExpr
                                                                                    else
                                                                                        sortedExpr <- sortedExpr@[sortedAG]
                                                        | AExponent (y,pwr2)    -> if pwr > pwr2 then
                                                                                        sortedExpr <- sortedAG::sortedExpr
                                                                                   else
                                                                                        sortedExpr <- sortedExpr@[sortedAG]
                                                        | ARoot(_,_) -> failwith "There should be no roots"
                                | ARoot(_,_) -> failwith "There should be no roots"
                      sortedExpr <- List.rev sortedExpr
                      SE sortedExpr

    (* Divides to expressions and returns the resulting expression
       e.g. 2x^2 / 1x^2 = 2x^0 *)
    let divide (cons,(var:string),pwr) (cons2,(var2:string),pwr2) = ((cons/cons2),var,(pwr-pwr2))  

    (* Simple helper function that takes a simpleExpr and returns it as a atomGroup list *)
    let simpleExprToListList simExpr =
        match simExpr with
            SE(se) -> se
    
    (* http://stackoverflow.com/questions/3974758/in-f-how-do-you-merge-2-collections-map-instances *)
    let join (p:Map<'a,'b>) (q:Map<'a,'b>) = 
        Map(Seq.concat [ (Map.toSeq p) ; (Map.toSeq q) ])

    (* Helper function that takes two integers and creates a map with a key,value pair for every integer in between the provided integers.  
       Keys are integers and values are simpleExprs *)
    let buildMissingExp prevExp exp =         
        let list = [(prevExp+1) .. (exp-1)]
        let resultList = 
            List.fold (fun acc i ->
                (i,(0.0))::acc
            ) List.Empty list
        Map.ofList resultList

    (* Helper function that takes a poly (as a map) and fills out the missing exponents between the lowest and highest exponent of the poly.
       Example; given the poly 2x^5+8x^0 the result would be 2x^5+0x^4+0x^3+0x^2+0x^1+8x^0 *)
    let fillPoly (mP:Map<int,float>) =
        let mutable prevExp = 0
        Map.fold (fun (updatedMap:Map<int,float>) exp f ->
            if exp > prevExp then
                if ((prevExp+1) = exp) then
                    prevExp <- exp
                    Map.add exp f updatedMap
                else
                    let tmpM = buildMissingExp prevExp exp
                    prevExp <- exp
                    join (updatedMap.Add(exp,f)) tmpM
            else
                prevExp <- exp
                Map.add exp f updatedMap
        ) Map.empty mP

    (* Helper function that takes a poly (as a map) and removes entries that are 0 *)
    let cleanPoly (p:Map<int,float>) =
        Map.fold (fun cleanMP exp f ->
            if not (f = 0.0) then
                Map.add exp f cleanMP
            else
            cleanMP
        ) Map.empty p

    (* Simple helper function that returns the highest exponent of a poly (poly as a map) *)
    let getDegree m =
        Map.fold(fun acc key value -> if acc < key then key else acc) -1 m

    (* Helper function that takes a tuple (representing a expr/atom) and multiples it into a map (map of a poly) *)
    let atomTimesPoly (cons,(var:string),pwr) m =
        Map.fold (fun acc exp f ->
                    let updatedf = f*cons
                    Map.add (exp+pwr) updatedf acc) Map.empty m

    (* Helper function that takes two polys (represented as two maps) and subtract the second poly from the first *)
    let polySubtract (p1:Map<int,float>) (p2:Map<int,float>) =
        Map.fold (fun acc exp f ->
            if (Map.containsKey exp p2) then
                let updatedf = f - (Map.find exp p2)
                Map.add exp updatedf acc
            else
                Map.add exp f acc) Map.empty p1

    (* Helper function that takes two polys (represented as two maps) and adds the second poly to the first *)
    let polyAdd (p1:Map<int,float>) (p2:Map<int,float>) =
        Map.fold (fun acc exp f ->
            if (Map.containsKey exp p2) then
                let updatedf = f + (Map.find exp p2)
                Map.add exp updatedf acc
            else
                Map.add exp f acc) Map.empty p1

    exception DivideByEmptyPolyException

    (* Polonomial long division function that takes two polys and returns a tuple of polys (q,r), 
       where q is the iterative result of the division while r is the remainder of the division after iteration *)
    let polyLongDiv (p1:Map<int,float>) p2 var = 
        if Map.isEmpty (p2) then 
            raise DivideByEmptyPolyException
        else
            let mutable result = List.Empty
            let mutable SEResult = List.Empty
            let mutable dividend = fillPoly p1
            let mutable divisor = fillPoly p2
            let mutable recur = 0

            while ((not (Map.isEmpty dividend)) && (getDegree dividend >= getDegree divisor)) do


                let divisorHighTerm = divisor.[(getDegree divisor)]

                let dividendNextTerm = dividend.[(getDegree dividend)]

                let (cons, var, pwr) = divide (dividendNextTerm,var,(getDegree dividend)) (divisorHighTerm,var,(getDegree divisor))
                let atomPoly = atomTimesPoly (cons, var, pwr) divisor
                let polyTract = polySubtract dividend atomPoly
                dividend <- polySubtract dividend (atomTimesPoly (cons, var, pwr) divisor)
                let dd = dividend.[(getDegree dividend)]
                if (dd = 0.0 || recur > 20) then
                    dividend <- Map.remove (getDegree dividend) dividend
                    
                recur <- recur+1    

            dividend

    let solvePoly p value =
        Map.fold (fun res exp f ->
            let mutable AGRes = 0.0
            AGRes <- (f* (pown value exp))+ AGRes
            res + AGRes
            ) 0.0 p

    let getSturmSequence p var value =
        let mutable sturmChain = List.Empty
        let mutable dividend = p
        let mutable divisor = getDerivative p
        sturmChain <- p::sturmChain
        sturmChain <- (getDerivative p)::sturmChain
        while (getDegree sturmChain.Head) > 0 do
            let r = (atomTimesPoly (-1.0,var,0) (cleanPoly (polyLongDiv dividend divisor var)))
            sturmChain <- r::sturmChain
            dividend <- divisor
            divisor <- r
        let sturmSeq = List.fold (fun seq p ->
                            (solvePoly p value)::seq
                       ) List.Empty sturmChain
        sturmSeq

    let signChanges (s1:float list)=
            let mutable isFirst = true
            let mutable previous = 0.0
            let mutable s1changes = 0.0
            for x in s1 do
                if isFirst then 
                    isFirst <- false
                    previous <- x else
                    if previous >= 0.0 && x < 0.0 || previous < 0.0 && x >= 0.0 then s1changes <- s1changes+1.0 
                                                                                     previous <- x else previous <- x
            s1changes

    let diff x y :float=
        if x > y then x - y else y - x
    
    let newton_raphson (p:Map<int,float>) a =
        let mutable a = a
        for x in [0..10] do 
            let xn = a - ((solvePoly p a)/(solvePoly (getDerivative p) a))
            a <- xn
        a

    let rec helpFinder poly sx x sy y recur :float =
        let z = ((y-x)*0.5) + x
        let midd = getSturmSequence poly "t" z
        let smidd = signChanges midd
        let diffsxmid = diff sx smidd
        let diffsymid = diff sy smidd
        if recur <= 10 then
            if diffsxmid < 1.5 && diffsxmid > 0.5 then newton_raphson poly (((z-x)*0.5) + x)
            elif diffsymid < 1.5 && diffsymid > 0.5 && diffsxmid < 1.0 then newton_raphson poly (((y-z)*0.5) + z) else
                 if diffsxmid > 1.0 then helpFinder poly sx x smidd z (recur+1)
                 else if diffsymid > 1.0 then helpFinder poly smidd z sy y (recur+1) else z
        else 
            newton_raphson poly z

    let findRoot poly =
        let zero = getSturmSequence poly "t" 0.0
        let onehun = getSturmSequence poly "t" 100.0
        let fifty = getSturmSequence poly "t" 50.0
        let szero = signChanges zero
        let sonehun = signChanges onehun
        let sfifty = signChanges fifty
        let diff050 = diff szero sfifty
        let diff50100 = diff sfifty sonehun
        if diff050 > 0.0 then Some(helpFinder poly szero 0.0 sfifty 50.0 1) else if diff50100 > 0.0 then Some(helpFinder poly sfifty 50.0 sonehun 100.0 1) else None

    let findTbox d x l h =
        if d >= 0.0 then let tx = (l - x)/d
                         let t'x = (h - x)/d 
                         (tx,t'x) else let tx = (h - x)/d
                                       let t'x = (l - x)/d
                                       (tx,t'x)

    let deriveForVar expr s =
        let (SE simExpr) = (simplifySimpleExpr expr)
        let cleanExpr = SE (List.map (fun ag -> List.rev ag) simExpr)
        match cleanExpr with 
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
                                        prevVal <- 1.0
                                    else
                                        prevList <- (ANum prevVal)::(AExponent (a,(exp)))::prevList
                                        if List.length derivAG <> 0 then (derivAG <- derivAG @ prevList)
                                        prevVal <- 1.0
                            | ARoot(_,_) -> failwith "There should be no roots"

                        deriv <- derivAG::deriv 
                    SE deriv   