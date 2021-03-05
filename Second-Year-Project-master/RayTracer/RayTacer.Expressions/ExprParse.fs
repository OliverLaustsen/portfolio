namespace RayTracer.Expressions

module ExprParse =

(* Grammar:

E    = T Eopt .
Eopt = "+" T Eopt | e .
T    = F Topt .
Topt = "*" F Topt | e .
F    = P Fopt .
Fopt = "^" Int | e .
P    = Int [ Float | Var | "(" E ")" .

e is the empty sequence.
*)

    type terminal =
        Add | Mul | Div | Pwr | Root | Lpar | Rpar | Int of int | Float of float | Var of string

    let isblank c = System.Char.IsWhiteSpace c
    let isdigit c  = System.Char.IsDigit c
    let isletter c = System.Char.IsLetter c
    let isletterdigit c = System.Char.IsLetterOrDigit c

    let explode s = [for c in s -> c]

    let floatval (c:char) = float((int)c - (int)'0')
    let intval(c:char) = (int)c - (int)'0'

    exception Scanerror

    let rec scnum (cs, value) = 
      match cs with 
        '.' :: c :: cr when isdigit c -> scfrac(c :: cr, (float)value, 0.1)
      | c :: cr when isdigit c && value < 0 -> scnum(cr, 10* value - intval c)
      | c :: cr when isdigit c -> scnum(cr, 10* value + intval c)
      | _ -> (cs,Int value)    (* Number without fraction is an integer. *)
    and scfrac (cs, value, wt) =
      match cs with
      | c :: cr when isdigit c -> if (value >= 0.0) then scfrac(cr, value+wt*floatval c, wt/10.0) else scfrac(cr, value-wt*floatval c, wt/10.0)
      | _ -> (cs, Float value)

    let rec scname (cs, value) =
      match cs with
        c :: cr when isletterdigit c -> (cs, value)
      | _ -> (cs, value)

    let scan s =
      let rec sc cs = 
        match cs with
          [] -> []
        | '+' :: cr -> Add :: sc cr  
        | '*' :: cr -> Mul :: sc cr      
        | '^' :: cr -> Pwr :: sc cr      
        | '_' :: cr -> Root :: sc cr
        | '/' :: cr -> Div :: sc cr
        | '(' :: cr -> Lpar :: sc cr     
        | ')' :: cr -> Rpar :: sc cr     
        | '-' :: c :: cr when isdigit c -> let (cs1, t) = scnum(cr, intval c)
                                           let c = Lpar::t::Mul::Float(-1.0)::Rpar::[]
                                           [Add] @ c @ sc cs1
        | '-' :: b :: c :: cr when isdigit c && isblank b -> let (cs1, t) = scnum(cr, -1 * intval c)
                                                             [Add;t] @ sc cs1
        | '-' :: c :: cr when isletter c -> let (cs1, n) = scname(cr, (string)c)
                                            [Add;Int -1;Mul;Var n] @ sc cs1
        | '-' :: b :: c :: cr when isletter c && isblank b -> let (cs1, n) = scname(cr, (string)c)
                                                              [Add;Int -1;Mul;Var n] @ sc cs1
        | '-' :: cr -> [Add;Int -1;Mul] @ sc cr
        | c :: cr when isdigit c -> let (cs1, t) = scnum(cr, intval c) 
                                    t :: sc cs1
        | c :: cr when isblank c -> sc cr      
        | c1 :: c2 :: cr when isletter c1 && isletter c2 -> let (cs1, n1) = scname(cr,(string)c1)
                                                            let (cs1, n2) = scname(cr,(string)c2)
                                                            let nn = n1 + n2
                                                            Var nn :: sc cs1                                                    
        | c :: cr when isletter c -> let (cs1, n) = scname(cr, (string)c)
                                     Var n :: sc cs1
        | _ -> raise Scanerror
      sc (explode s)
    
    let rec insertMult = function
      Float r :: Var x :: ts -> Float r :: Mul :: insertMult (Var x :: ts)
    | Float r1 :: Float r2 :: ts -> Float r1 :: Mul :: insertMult (Float r2 :: ts)
    | Float r :: Int i :: ts -> Float r :: Mul :: insertMult (Int i :: ts)
    | Var x :: Float r :: ts -> Var x :: Mul :: insertMult (Float r :: ts)
    | Var x1 :: Var x2 :: ts -> Var x1 :: Mul :: insertMult (Var x2 :: ts)
    | Var x :: Int i :: ts -> Var x :: Mul :: insertMult (Int i :: ts)
    | Int i :: Float r :: ts -> Int i :: Mul :: insertMult (Float r :: ts)
    | Int i :: Var x :: ts -> Int i :: Mul :: insertMult (Var x :: ts)
    | Int i1 :: Int i2 :: ts -> Int i1 :: Mul :: insertMult (Int i2 :: ts)
    | Float r :: Lpar :: ts -> Float r :: Mul :: insertMult (Lpar :: ts)
    | Var x :: Lpar :: ts -> Var x :: Mul :: insertMult (Lpar :: ts)
    | Int i :: Lpar :: ts -> Int i :: Mul :: insertMult (Lpar :: ts)
    | Rpar :: Lpar :: ts -> Rpar :: Mul :: Lpar :: insertMult(ts)
    | Rpar :: Int i :: ts -> Rpar :: Mul :: Int i :: insertMult (ts)
    | Rpar :: Var x :: ts -> Rpar :: Mul :: Var x :: insertMult (ts)
    | t :: ts -> t :: insertMult ts
    | [] -> []
  
    type expr = 
      | FNum of float
      | FVar of string
      | FAdd of expr * expr
      | FMult of expr * expr
      | FDiv of expr * expr
      | FExponent of expr * int
      | FRoot of expr * int
      
    exception Parseerror

    let rec E (ts:terminal list) = (T >> Eopt) ts
    and Eopt (ts, inval) =
      match ts with
        Add :: tr -> let (ts1, tv) = T tr
                     Eopt (ts1, FAdd(inval,tv))
       | _ -> (ts,inval)
    and T ts = (F >> Topt) ts
    and Topt (ts, inval) = 
      match ts with
        Mul :: tr -> let (ts1, fv) = F tr
                     Topt (ts1, FMult(inval,fv))
       | Div :: tr -> let (ts1, fv) = F tr
                      Topt (ts1, FDiv(inval,fv))
       | _ -> (ts, inval)
    and F ts = (P >> Fopt) ts
    and Fopt (ts, inval) = 
      match ts with
        Pwr :: tr -> 
            match tr with
                |Int x :: s -> (s, FExponent(inval, x))
                | _ -> raise Parseerror 
        | Root :: tr -> 
            match tr with
                |Int x :: s -> (s, FRoot(inval, x))
                | _ -> raise Parseerror 
        | _ -> (ts, inval)
    and P ts = 
      match ts with
        |a :: Int i :: tr when a = Add -> (tr,FNum(float i))
        |Int i :: tr -> (tr, FNum(float i))
        |Float f :: tr -> (tr,FNum f)
        |Var v :: tr -> (tr, FVar v)
        |a :: LPar :: tr when a = Add ->let (ts1, ev) = E tr
                                        match ts1 with
                                           Rpar :: tr -> (tr,ev)
                                           | _ -> raise Parseerror
        |Lpar :: tr -> let (ts1, ev) = E tr
                       match ts1 with
                          Rpar :: tr -> (tr,ev)
                          | _ -> raise Parseerror
        | _ -> raise Parseerror

    let parse (ts:terminal list) =
      match E ts with
        ([], result) -> result
      | _ -> raise Parseerror

    let parseStr s = (scan >> insertMult >> parse) s