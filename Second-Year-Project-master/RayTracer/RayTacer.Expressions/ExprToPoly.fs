namespace RayTracer.Expressions
open ExprParse
module ExprToPoly =

    type expr = ExprParse.expr

    let rec ppExpr = function
      | FNum c -> string(c)
      | FVar s -> s
      | FAdd(e1,e2) -> "(" + (ppExpr e1) + " + " + (ppExpr e2) + ")"
      | FMult(e1,e2) -> (ppExpr e1) + " * " + (ppExpr e2)
      | FDiv(e1,e2) -> (ppExpr e1) + " / " + (ppExpr e2)
      | FExponent(e,n) -> "(" + (ppExpr e) + ")^" + string(n)
      | FRoot(e,n) -> "(" + (ppExpr e) + ")_" + string(n)

    let rec subst e (x,ex) =
      match e with    
      | FNum c -> FNum c
      | FVar s -> if s = x then ex else e
      | FAdd (a,b) -> let aa = subst a (x,ex)
                      let bb = subst b (x,ex)
                      FAdd(aa,bb)
      | FMult (a,b) -> let aa = subst a (x,ex)
                       let bb = subst b (x,ex)
                       FMult(aa,bb)
      | FExponent (a,g) -> let aa = subst a (x,ex)
                           FExponent (aa,g)
      | FRoot (a,g) -> let aa = subst a (x,ex)
                       FRoot (aa,g)
      | FDiv (_,_) -> failwith "There should be no division"

    type atom = ANum of float | AExponent of string * int | ARoot of (atom list list) * int
    type atomGroup = atom list  
    type simpleExpr = SE of atomGroup list
    let isSimpleExprEmpty (SE ags) = ags = [] || ags = [[]]

    let ppAtom = function
      | ANum c -> string(c)
      | AExponent(s,1) -> s
      | AExponent(s,n) -> s+"^"+(string(n))
      | ARoot (_,_) -> failwith "Pretty printing roots not implemented"
    let ppAtomGroup ag = String.concat "*" (List.map ppAtom ag)
    let ppSimpleExpr (SE ags) = String.concat "+" (List.map ppAtomGroup ags)

    let rec combine xss = function
      | [] -> []
      | ys::yss -> List.map ((@) ys) xss @ combine xss yss

    let getNominatorDenominator e =
        match e with
            | FDiv(e1,e2) -> (e1,e2)
            | _ -> failwith "Unexpected input"

    let rec divExprToBigDiv e =
        match e with
            | FDiv(e1,e2) -> 
                let (e3,e4) = divExprToBigDiv e1 |> getNominatorDenominator
                let (e5,e6) = divExprToBigDiv e2 |> getNominatorDenominator
                FDiv(FMult(e3,e6),FMult(e4,e5))
            | FAdd(e1,e2) ->
                let (e3,e4) = divExprToBigDiv e1 |> getNominatorDenominator
                let (e5,e6) = divExprToBigDiv e2 |> getNominatorDenominator
                FDiv(FAdd(FMult(e3,e6),FMult(e4,e5)),FMult(e4,e6))
            | FMult(e1,e2) ->
                let (e3,e4) = divExprToBigDiv e1 |> getNominatorDenominator
                let (e5,e6) = divExprToBigDiv e2 |> getNominatorDenominator
                FDiv(FMult(e3,e5),FMult(e4,e6))
            | FNum c -> FDiv(FNum c, FNum 1.0)
            | FVar s -> FDiv(FVar s, FNum 1.0)
            | FExponent(e,n) ->  
                let (e1,e2) = divExprToBigDiv e |> getNominatorDenominator
                FDiv(FExponent(e1,n),FExponent(e2,n))
            | FRoot(e1,n) -> FDiv(FRoot(e1,n), FNum 1.0)

    let rec simplify = function
      | FNum c -> [[ANum c]]
      | FVar s -> [[AExponent(s,1)]]
      | FAdd(e1,e2) -> simplify e1 @ simplify e2
      | FMult(e1,e2) -> combine (simplify e1) (simplify e2)
      | FDiv(e1,e2) -> (simplify e1)
      | FExponent(e1,0) -> [[ANum 1.0]]
      | FExponent(e1,n) -> simplify (FMult(e1, FExponent(e1,(n-1))))
      | FRoot(e1,n) -> [[ARoot(simplify e1,n)]]
    
    let simplifyRootsinAG (ag:atomGroup) (useContainRoots:bool)=
        let mutable rootList = List.Empty
        let mutable restList = List.Empty
        for atom in ag do
            match atom with
                | ANum n -> restList <- (ANum n)::restList
                | AExponent (name,y) -> restList <- (AExponent (name,y))::restList
                | ARoot (exp,r) -> rootList <- (ARoot(exp,r))::rootList
        let mutable usedRestList = false
        let mutable first = true
        let simplifiedRoots =
            List.fold (fun simRootList root ->
                let nrOfRoots =
                    List.fold (fun count cmp ->
                        if root = cmp then (count+1) else count
                    ) 0 rootList
                if nrOfRoots > 1 then
                    match root with
                        | ARoot(exp,r) ->
                            if nrOfRoots = r then
                                if restList.Length > 0 then
                                    let res =
                                        List.fold (fun simSE atom ->
                                            match atom with
                                                | ANum n ->
                                                    if not (n = 1.0) || first then
                                                        first <- false
                                                        let res2 = 
                                                            List.fold (fun sE AG ->
                                                                (atom::AG)::sE
                                                            ) List.Empty exp
                                                        if List.contains res2 simSE then simSE
                                                        else res2::simSE
                                                    else simSE
                                                | _ ->
                                                    let res2 = 
                                                        List.fold (fun sE AG ->
                                                            (atom::AG)::sE
                                                        ) List.Empty exp
                                                    if List.contains res2 simSE then simSE
                                                    else res2::simSE
                                        ) List.Empty restList
                                    usedRestList <- true
                                    if List.contains res simRootList then simRootList
                                    else res::simRootList
                                else [[exp]]
                            else [[[[ARoot(exp,r)]]]]@simRootList      
                        | _ -> failwith "Program should not hit this point"                        
                elif nrOfRoots = 1 then
                    [[[root]]]::simRootList
                else failwith "Root was expected to exist"
            ) List.Empty rootList

        let result =
            if usedRestList then simplifiedRoots
            else [[[restList]]]@simplifiedRoots
        
        let mutable cleanedRes = List.Empty
        for list in result do
            for se in list do
                for ag in se do
                    cleanedRes <- ag::cleanedRes

        let containsRoot = 
            if useContainRoots then
                List.exists (fun list -> 
                    let res = 
                        List.exists (fun atom ->
                            let bool =
                                match atom with
                                    | ARoot (_,_) -> true
                                    | _ -> false
                            bool
                        ) list
                    res
                ) cleanedRes
            else false

        if containsRoot then
            let newList =
                List.fold (fun res list -> res@list) List.Empty cleanedRes
            [newList]
        else
            cleanedRes

    let addSimRootToAG (simRoots:atom list list list) =
        SE (List.fold (fun se simRootsSE ->
            let tmp =
                List.fold (fun agList ag -> ag::agList) List.Empty simRootsSE
            tmp@se
           ) List.Empty simRoots)

    let onlyRoots (SE simExpr) = 
            (List.exists (fun list -> 
                let res = 
                    List.exists (fun atom ->
                        let bool =
                            match atom with
                                | ARoot (_,_) -> true
                                | ANum 1.0 -> true
                                | _ -> false
                        bool
                    ) list
                res
            ) simExpr)

    let agContainsRoot ag =
        List.exists (fun atom -> 
            match atom with
                | ARoot(_,_) -> true
                | _ -> false
        ) ag

    let cleanAG ag = 
        List.fold (fun cleanedAG atom ->
            match atom with
            | ANum 1.0 -> cleanedAG
            | _ -> atom::cleanedAG
        ) List.Empty ag

    let agsAreEqual ag1 ag2 =
        let cleanAG1 = cleanAG ag1
        let cleanAG2 = cleanAG ag2
        if not (cleanAG1.Length = cleanAG2.Length) then
            false
        else
            List.fold (fun isEqual atom ->
                let res = 
                    List.exists (fun atom2 ->
                        atom = atom2
                    ) cleanAG2
                res
            ) false cleanAG1

    
    let mutable sum = 1.0

    let rec removeRoots (SE se) =
        let mutable leftSide = List.Empty
        let mutable rightSide =
            List.fold (fun right ag ->
                if (List.length ag > 0) then
                    let contains = agContainsRoot ag
                    if contains then
                        leftSide <- ag::leftSide
                        right
                    else 
                        ((ANum -1.0)::ag)::right
                else
                    right
            ) List.Empty se
        
        let pureRoots = onlyRoots (SE leftSide)
        
        let mutable newLeftSide = List.Empty
        let mutable newRightSide = List.Empty

        if pureRoots then
            let largestRoot =
                List.fold (fun root ag ->
                    let rootSize =
                        List.fold (fun atomRoot atom ->
                            let atomRootSize =
                                match atom with
                                | ARoot(e,n) -> 
                                    if n > root then n else root
                                | _ -> atomRoot
                            if atomRootSize > root then atomRootSize else root
                        ) 0 ag
                    if rootSize > root then rootSize else root
                ) 0 leftSide
            if largestRoot <= 1 then failwith "Expected root of degree 2"
            elif largestRoot = 2 then
                if (leftSide.Length > 1) then
                    let tmpList =
                        List.fold (fun simL ag ->
                            (simplifyRootsinAG ag true)::simL
                        ) List.Empty leftSide
                    let (SE cleanTmp) = addSimRootToAG tmpList
                    let mutable left = List.Empty
                    let res =
                        List.map (fun ag ->
                            let contains = agContainsRoot ag
                            if contains then
                                left <- ag::left
                            else 
                                rightSide <- ((ANum -1.0)::ag)::rightSide
                        ) cleanTmp

                    let simLeft =
                        let mutable outerI = 0
                        let mutable usedList = List.Empty
                        let mutable amountOfEquals = 0.0
                        List.fold (fun sL ag ->
                            outerI <- outerI + 1
                            let mutable amountOfEquals = 0.0
                            let mutable innerI = 0
                            if not (List.contains ag usedList) then
                                let tmpUsedList =
                                    List.fold (fun used ag2 ->
                                        innerI <- innerI + 1
                                        if not (outerI > innerI) then
                                            let equal = agsAreEqual ag ag2
                                            if equal then
                                                amountOfEquals <- amountOfEquals + 1.0
                                                if (List.contains ag usedList) then
                                                    ag2::used
                                                else 
                                                    ag::ag2::used
                                            else used
                                        else used
                                    ) List.Empty left

                                usedList <- tmpUsedList@usedList
                                ((ANum amountOfEquals)::ag)::sL
                            else
                                sL
                                
                        ) List.Empty left
                    if simLeft.Length = 1 then
                        let simpleLeft =
                            List.fold (fun cleanAG atom ->
                                match atom with
                                | ARoot(_,_) -> atom::cleanAG
                                | ANum n -> 
                                    sum <- sum * n
                                    cleanAG
                                | _ -> cleanAG
                            ) List.Empty simLeft.[0]
//                        rightSide <- List.fold (fun newRight ag -> ((ANum (1.0/(sum)))::ag)::newRight) List.Empty rightSide
                        leftSide <- [simpleLeft]
                    else
                        leftSide <- simLeft
                elif (leftSide.Length = 1) then
                    let ag = leftSide.[0]
                    let mutable lastRoot = ARoot([[]],0)
                    let nrOfRoots = 
                        List.fold (fun rootInAG atom ->
                            match atom with
                                | ARoot(e,n) -> 
                                    lastRoot <- ARoot(e,n)
                                    rootInAG + 1
                                | _ -> rootInAG
                        ) 0 ag
                    if nrOfRoots > 1 then
                        let identicalRoot =
                            List.fold (fun sameRoots atom ->
                                atom = lastRoot
                            ) false ag
                        if identicalRoot then
                            if nrOfRoots = 2 then
                                match lastRoot with
                                | ARoot(e,n) -> leftSide <- e
                                | _ -> failwith "There should only be roots"
                            else failwith "Can only simplify roots of type a_2 * a_2, not a_2 * a_2 * a_2"
                        else failwith "Currently the program does not support different types of roots in a atomGroup"
                    elif nrOfRoots = 1 then
                        if ((cleanAG ag).Length = 1) then
                            newLeftSide <-
                                List.fold (fun newLeft list ->
                                    (list@list)::newLeft
                                ) List.Empty leftSide
                            newRightSide <-
                                List.fold (fun newRight list ->
                                    (list@list)::newRight
                                ) List.Empty rightSide
                            let tmpLeft = List.map (fun ag -> simplifyRootsinAG ag false) newLeftSide
                            let (SE tmpLeft2) = addSimRootToAG tmpLeft
                            leftSide <- tmpLeft2
                            let tmpRight = List.map (fun ag -> simplifyRootsinAG ag false) newRightSide
                            let (SE tmpRight2) = addSimRootToAG tmpRight
                            rightSide <- tmpRight2
                        elif ((cleanAG ag).Length > 1) then
                            let cAG = cleanAG ag
                            let cleanLeft = 
                                List.fold (fun cL atom ->
                                    match atom with
                                    | ARoot(e,n) -> (ARoot(e,n))::cL
                                    | _ -> cL
                                ) List.Empty cAG
                            newLeftSide <-
                                List.fold (fun newLeft list ->
                                    (list@list)::newLeft
                                ) List.Empty [cleanLeft]
                            newRightSide <-
                                List.fold (fun newRight list ->
                                    (list@list)::newRight
                                ) List.Empty rightSide
                            let tmp = List.map (fun ag -> simplifyRootsinAG ag false) newLeftSide
                            let (SE tmp2) = addSimRootToAG tmp
                            leftSide <- tmp2
                        else
                            failwith "List was not meant to be empty"
                    else failwith "Expected a root"
                else failwith "Left side was expected to contain values"
            else failwith "Expected root of degree 2"
            let (SE l,SE r) = removeRoots (SE leftSide)
            rightSide <- List.fold (fun newRight ag -> ((ANum (1.0/(sum)))::ag)::newRight) List.Empty rightSide
            let resRight = r@rightSide
            let resLeft = l
            (SE resLeft, SE resRight)
            
        else
            let finalLeft =
                List.fold (fun result ag ->
                    (([ANum -1.0])@ag)::result
                ) List.Empty rightSide
            (SE finalLeft, SE [[ANum 0.0]])

    let resultOfRemoveRoots (SE left,SE right) =
        let powRight = 
            List.fold (fun pow ag ->
                let newAG =
                    List.fold (fun powAG ag2 ->
                        (ag@ag2)::powAG
                    ) List.Empty right
                newAG@pow
            ) List.Empty right
        let res =
            SE (List.fold (fun result ag ->
                (([ANum -1.0])@ag)::result
                ) left powRight)
        res

    let simplifyAtomGroup ag = 
        let m = Map.empty
        let aexp = List.fold (fun acc atom -> 
            match atom with
            |   AExponent (name,y) -> 
                    if Map.containsKey name acc then acc.Add(name,(acc.Item(name)+(float y))) else acc.Add(name,(float y))
            |   ANum x -> 
                    if Map.containsKey "ANum" acc then acc.Add("ANum",(acc.Item("ANum")*x)) else acc.Add("ANum",x)
            |   ARoot (_,_) -> failwith "There should be no roots"
                    ) m ag

        let ress1 = Map.fold (fun acc k v -> if k = "ANum" then 
                                                let a = ANum (float v) 
                                                a::acc
                                             else 
                                                let a = AExponent (k,(int v)) 
                                                a::acc) [] aexp

        let ress = ress1
        ress

    let simplifySimpleExpr (SE ags) =
      let ags' = List.map simplifyAtomGroup ags
      // Add atom groups with only constants together.
      let m = Map.empty
      let rec conss acc ags r =
        match ags with
            |[ANum y]::res -> conss (acc + y) res r
            |a::res -> (conss acc res (a::r))
            |[] -> (acc,r)
      let cc = conss 0.0 ags' []
      let constant = fst cc
      let ags' = snd cc
      // Last task is to group similar atomGroups into one group.
      let rec expo acc ags = 
        match ags with
          | a::rest when Map.containsKey a acc -> 
                                          let x = Map.find a acc
                                          let something = (acc.Add(a,x + 1.0))
                                          expo something rest
          | a::rest -> expo (acc.Add(a,1.0)) rest
          | _ -> acc
      let exponent = expo m ags'
      let ress = List.map (fun (x, y) -> if y = 1.0 then x else ANum(y)::x) (Map.toList exponent)
      let foo = [ANum constant]
      SE (foo::ress)
      
    let exprToSimpleExpr e = 

        let simExpr = 
            let se = simplify e
            let simRoots = List.map (fun ag -> simplifyRootsinAG ag true) se
            let newSE = addSimRootToAG  simRoots
            (removeRoots newSE) |> resultOfRemoveRoots
        simplifySimpleExpr simExpr
        

    let subSimple agl (replaceMap:Map<string,float>) =
        let newAGL = List.map simplifyAtomGroup agl
        let mutable polymap = Map.empty
        let reducedSE = 
            List.fold (fun newSE AG ->
                let newAG = 
                    List.fold (fun updatedAG atom ->
                        let newAtom = 
                            match atom with
                            | ANum n -> ANum(n)
                            | AExponent(v,e) when v = "dx" -> if Map.containsKey v replaceMap then 
                                                                ANum (replaceMap.[v]**(float e)) 
                                                              else AExponent(v,e)
                            | AExponent(v,e) -> if Map.containsKey v replaceMap then ANum (replaceMap.[v]**(float e)) else AExponent(v,e)
                            | (_) -> failwith "expected an atom type"
                        newAtom::updatedAG
                    ) [] AG
                newAG::newSE
            ) [] agl
        for AG in reducedSE do
            let mutable constant = 1.0
            let mutable exponent = 0
            for atom in AG do
                match atom with
                | ANum x -> constant <- constant * x
                | AExponent (v,e) -> exponent <- e
                | (_) -> failwith "expected an atom type"
            if Map.containsKey exponent polymap then
                let x = Map.find exponent polymap
                polymap <- polymap.Add(exponent,x+constant)                                              
            else 
                polymap <- polymap.Add(exponent,constant)

        polymap

    (* Collect atom groups into groups with respect to one variable v *)
    let splitAG v m = function
      | [] -> m
      | ag ->
        let eqV = function AExponent(v',_) -> v = v' | _ -> false
        let addMap d ag m = 
            match Map.tryFind d m with
            |   Some (SE(x))  -> m.Add(d, SE(ag::x))
            |   None -> m.Add(d, SE([ag]))
        match List.tryFind eqV ag with
          | Some (AExponent(_,d)) ->
            let ag' = List.filter (not << eqV) ag
            addMap d ag' m
          | Some _ -> failwith "splitAG: Must never come here! - ANum will not match eqV"
          | None -> addMap 0 ag m
    
    let rec agToExpr ag =
        match ag with
            | a :: rAG when ag.Length > 1 -> 
                match a with
                    | ANum n -> FMult((FNum n),(agToExpr rAG))
                    | AExponent(v,n) -> FMult((FExponent((FVar v),n)),(agToExpr rAG))
                    | _ -> failwith "Unexpected ARoot"
            | a :: rAG when ag.Length = 1 ->
                match a with
                    | ANum n -> (FNum n)
                    | AExponent(v,n) -> (FExponent((FVar v),n))
                    | _ -> failwith "Unexpected ARoot"
            | (_) -> failwith "expected an atom list type"
    let rec simpleExprToExpr (SE se) =
        match se with
            | ag :: rSE when se.Length = 1 -> agToExpr se.[0]
            | ag :: rSE when se.Length > 1 -> FAdd((agToExpr ag), (simpleExprToExpr (SE rSE)))
            | (_) -> failwith "expected an atomGroup type"
