namespace RayTracer.TriangleParser
open RayTracer.Entities
open RayTracer.TriangleParser.Helpers
open RayTracer.TriangleParser.HeaderParser
open RayTracer.TriangleParser.TxtParser
open FParsec
open Colour
open RayTracer.Shapes.Shape
open Point
open System.IO
open System 

module TriangleParser =
    let parsePly (file:string) =
        let (endpos,header) = parseHeader file
        
        let isBigEnd = header |> HeaderParser.isBigEnd
        let isSmallEnd = header |> HeaderParser.isSmallEnd
        let isTxt = header |> HeaderParser.isTxt

        if(isTxt) then 
            parseTxt header file
        else if(isBigEnd || isSmallEnd) then
            BinaryParser.parseBinary (endpos,header) file
        else 
            raise (HeaderParserExpception "something went wrong after parsing the header")