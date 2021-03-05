namespace RayTracer.TriangleParser
open FParsec
open RayTracer.TriangleParser.Helpers
open System.IO
open System 


module HeaderParser =  
    type Format = TXT | BIGEND | LITEND
    type DataType = Float | UChar | List of DataType * DataType | Int
    type Property =  X | Y | Z | NX | NY | NZ | U | V | Triangles | Other
    type Attribute = Attribute of (DataType * Property)
    type HeaderInfo = TriangleCount of int | VerticeCount of int | Format of Format

    type Header = Header of (HeaderInfo list * Attribute list * Attribute list) 
    
    val parseHeader : string -> (int*Header)

    val getSkippableVerticeBytes : Attribute list -> (int*int)
    val getSkippableTriangleBytes : Attribute list -> (int*int)

    val getVerticeAtt : Header -> Attribute list
    val getTriangleAtt : Header -> Attribute list
    val getInfo : Header -> HeaderInfo list

    val validPly : Header -> bool
    val hasUV : Header -> bool
    val hasN : Header -> bool
    val getTriangleCount : Header -> int
    val getVerticeCount : Header -> int
    val getFormat : Header -> Format
    val isTxt : Header -> bool
    val isBigEnd : Header -> bool
    val isSmallEnd : Header -> bool