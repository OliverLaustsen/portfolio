namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.TriangleParser.Helpers
open FParsec
open Colour
open RayTracer.Shapes.Shape
open Point
open System.IO
open System 

module HeaderParser =
    type Format = TXT | BIGEND | LITEND
    type DataType = Float | UChar | List of DataType * DataType | Int
    type Property =  X | Y | Z | NX | NY | NZ | U | V | Triangles | Other
    type Attribute = Attribute of (DataType * Property)
    type HeaderInfo = TriangleCount of int | VerticeCount of int | Format of Format
    type Header = Header of (HeaderInfo list * Attribute list * Attribute list) 

    let getVerticeAtt (Header(_,v,_)) = v
    let getTriangleAtt (Header(_,_,t)) = t
    let getInfo (Header(i,_,_)) = i

    let pPropertyN : Parser<Property,_> =
        fun stream ->      
            let rs = stream.ReadRestOfLine(true)
            match rs with
                | "x" -> Reply (X)
                | "y" -> Reply (Y)
                | "z" -> Reply (Z)
                | "nx" -> Reply (NX)
                | "ny" -> Reply (NY)
                | "nz" -> Reply (NZ)
                | "u" -> Reply (U)
                | "v" -> Reply (V)
                | "vertex_indices" -> Reply (Triangles)
                | _ -> Reply (Other)


    //General datatype parser
    let pDataTypeGeneral (v :string) (ret : DataType) : Parser<DataType,_> =
        fun stream -> 
            if(stream.Skip(v))
            then Reply (ret)
            else Reply(Error,expectedString "Couldn't parse the format")

    let pDataTypeGeneralApplied (v :string) (ret : DataType) = attempt (ws >>. (pDataTypeGeneral v ret) .>> ws)

    let pFloatD = pDataTypeGeneralApplied "float" Float
    let pFloat32D = pDataTypeGeneralApplied "float32" Float
    let pUcharD = pDataTypeGeneralApplied "uchar" UChar
    let pIntD = pDataTypeGeneralApplied "int" Int
    let puint8D = pDataTypeGeneralApplied "uint8" Int
    let pint32D = pDataTypeGeneralApplied "int32" Int
    let puintD = pDataTypeGeneralApplied "uint" Int


    let pDataTypes = pint32D <|> pFloat32D <|> pFloatD <|> pUcharD <|> pIntD <|> puintD <|> puint8D
    
    let pListD : Parser<DataType,_> = 
        let p = str "list" >>. tuple2 pDataTypes pDataTypes
        fun stream -> 
            let line = stream.ReadRestOfLine true
            let res = run p line
            match res with 
                | Success((count,vertices),_,_) -> Reply (List (count,vertices))
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)


    let pDataType = (attempt pListD) <|> pDataTypes
    let pProperty = str "property " >>. tuple2 (pDataType .>> ws) pPropertyN

    let start line string =
        let s = string
        let p = str s .>> ws >>. pint32
        match run p line with
            | Success(count,_,_) -> (true,count)
            | Failure(errorMsg,_,_) -> (false,0)

    let isVerticeStart line = start line "element vertex"

    let isTriangleStart line = start line "element face"


    //TriangleCount
    let pTCount : Parser<HeaderInfo,_> =
        let s = "element face "
        let pC = str s >>. pint32
        fun stream ->      
            let res = run pC (stream.ReadRestOfLine(true))
            match res with
                | Success(count,_,_) -> Reply (TriangleCount(count))
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)

    //Verticecount
    let pVCount : Parser<HeaderInfo,_> =
        let s = "element vertex "
        let pC = str s >>. pint32
        fun stream ->      
            let res = run pC (stream.ReadRestOfLine(true))
            match res with
                | Success(count,_,_) -> Reply (VerticeCount(count))
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)

    //Formats
    let pFormats : Parser<Format,_> =
        fun stream ->      
            let line = stream.ReadRestOfLine(true)
            match line with
                | "format ascii 1.0" -> Reply (TXT)
                | "format binary_big_endian 1.0" -> Reply (BIGEND)
                | "format binary_little_endian 1.0" -> Reply (LITEND)
                | _ -> Reply(Error,expectedString "Expected a valid format")

    let pFormat : Parser<HeaderInfo,_> = 
        let p = pFormats
        fun stream ->      
            let res = run p (stream.ReadRestOfLine(true))
            match res with
                | Success(format,_,_) -> Reply ( Format(format) )
                | Failure(errorMsg,_,_) -> Reply(Error,expectedString errorMsg)

    let pHeaderInfo =(attempt pVCount) <|> (attempt pTCount) <|> (attempt pFormat)

    let updateProperty line (attributes : Attribute list) = 
        let res = run pProperty line
        match res with 
            | Success((t,p),_,_) -> (Attribute(t,p))::attributes
            | Failure(errorMsg,_,_) -> attributes

    let updateInfo line (info : HeaderInfo list) = 
        let res = run pHeaderInfo line
        match res with 
            | Success(i,_,_) -> i::info
            | Failure(errorMsg,_,_) -> info

    let rec pHeader (reader:StreamReader) (parsingVertice) (parsingTriangle) (pos:int) (header:Header) : (int*Header) =
        let line = reader.ReadLine()
        let newPos =  pos + line.Length + 1
        if (line = "end_header") then (newPos,header)
        else 
            let (vStart,vCount) = line |> isVerticeStart 
            let (tStart,tCount) = line |> isTriangleStart
            let nParsingVertice = (vStart) || (parsingVertice && not(tStart))
            let nParsingTriangle = (tStart) || (parsingTriangle)
            let infos = getInfo header
            let vatts = getVerticeAtt header
            let tatts = getTriangleAtt header
         
            if(parsingVertice && (not tStart)) then 
                let nvatts = updateProperty line vatts
                pHeader reader nParsingVertice nParsingTriangle newPos (Header(infos,nvatts,tatts))
            else if(parsingTriangle) then
                let ntatts = updateProperty line tatts
                pHeader reader nParsingVertice nParsingTriangle newPos (Header(infos,vatts,ntatts))
            else
                let ninfos = updateInfo line infos
                pHeader reader nParsingVertice nParsingTriangle newPos (Header(ninfos,vatts,tatts))
    
    
    let listContainsProperty (list:Attribute List) property = 
        let mutable contains = false
        for Attribute(_,p) in list do
            if(p = property) then contains <- true
        contains

    //Helpers for validating the header result
    let validPly (Header(info,v,t)) = X |> listContainsProperty v && Y |> listContainsProperty v  && Z |> listContainsProperty v
    let hasUV (Header(info,v,t)) = U |> listContainsProperty v  && V |> listContainsProperty v
    let hasN (Header(info,v,t)) = NX |> listContainsProperty v && NY |> listContainsProperty v  && NZ |> listContainsProperty v
         
    //Helpers for binary parsing
    let rec getBytes (d:DataType) = 
        match d with
        | Float -> 4
        | UChar -> 1
        | List(d,_) -> getBytes d
        | Int -> 4

    let rec canSkip (atts:Attribute list) isBefore (before,after) = 
        match atts with 
                [] -> (before,after)
                | Attribute(d,p)::rest -> 
                    let nIsBefore = (p = Other && isBefore)
                    if(isBefore && p = Other) then
                        let nbefore = before + getBytes d
                        canSkip rest nIsBefore (nbefore,after)
                    else if((not isBefore) && p = Other) then
                        let nafter = after + getBytes d
                        canSkip rest nIsBefore (before,nafter)
                    else 
                        canSkip rest nIsBefore (before,after)                                 
    
    let rec canTriSkip (atts:Attribute list) isBefore (before,after) = 
        match atts with 
                [] -> (before,after)
                | Attribute(d,p)::rest -> 
                    match d with
                        List(ld,_) -> 
                            let nIsBefore = false
                            let nbefore = (getBytes ld) + before
                            canTriSkip rest nIsBefore (nbefore,after)
                        | d -> 
                            if(isBefore) then
                                let nbefore = before + getBytes d
                                canTriSkip rest isBefore (nbefore,after)
                            else if((not isBefore)) then
                                let nafter = after + getBytes d
                                canSkip rest isBefore (before,nafter)
                            else 
                                canSkip rest isBefore (before,after) 
                        

    let getSkippableVerticeBytes (attributes : Attribute list) = canSkip attributes true (0,0)
    let getSkippableTriangleBytes (attributes : Attribute list) = canTriSkip attributes true (0,0)

    let parseHeader (path:string) = 
        use reader = new StreamReader(path)
        let header = Header( List.Empty, List.Empty, List.Empty )
        let (c,Header(i,v,t)) = pHeader reader false false 0 header
        let ts = getSkippableVerticeBytes (List.rev v)
        let trs = getSkippableTriangleBytes (List.rev t)
        (c,Header(i,List.rev v, List.rev t))    
    
    let getTriangleCount header = 
                let info = getInfo header
                let c = info |> List.find (fun info -> match info with 
                                                            TriangleCount(v) -> true 
                                                            | _ -> false)
                match c with 
                TriangleCount(v) -> v
                | _ -> 0

    let getVerticeCount header = 
                let info = getInfo header
                let c = info |> List.find (fun info -> match info with 
                                                            VerticeCount(v) -> true 
                                                            | _ -> false)
                match c with 
                VerticeCount(v) -> v
                | _ -> 0
    
    let isTxt header = 
                let info = getInfo header
                let format = info |> List.find (fun info -> match info with 
                                                            Format(format) -> true 
                                                            | _ -> false)
                match format with
                    Format(f) -> match f with
                                 TXT -> true
                                 | _ -> false
                    | _ -> false

    let isBigEnd header = 
                let info = getInfo header
                let format = info |> List.find (fun info -> match info with 
                                                            Format(format) -> true 
                                                            | _ -> false)
                match format with
                    Format(f) -> match f with
                                 BIGEND -> true
                                 | _ -> false
                    | _ -> false

    let isSmallEnd header = 
                let info = getInfo header
                let format = info |> List.find (fun info -> match info with 
                                                            Format(format) -> true 
                                                            | _ -> false)
                match format with
                    Format(f) -> match f with
                                 LITEND -> true
                                 | _ -> false
                    | _ -> false

    let getFormat header = 
                let info = getInfo header
                let c = info |> List.find (fun info -> match info with 
                                                            Format(f) -> true 
                                                            | _ -> false)
                match c with 
                Format(f) -> f
                | _ -> raise (HeaderParserExpception ("No format"))
        