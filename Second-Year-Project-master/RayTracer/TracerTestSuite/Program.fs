open TracerTestSuite

let allTargets : Target list =
  List.concat 
    [
     Shapes.render;
     AffineTransformations.render true;
     AffineTransformations.render false;
     ImplicitSurfaces.render;
     Meshes.render;
     Texture.render;
     Light.render;
     CSG.render;
     // The test group below is only needed for teams of 7 students.
     // Teams of 6 students can uncomment the line below.
     //ThinLens.render;
     ]


let renderAll (toScreen : bool) : unit = 
  List.iter (Util.renderTarget toScreen) allTargets
let renderTests (toScreen : bool) (group : string) (tests : string list) : unit = 
  Util.renderTests toScreen allTargets group tests
let renderGroups (toScreen : bool) (groups : string list) : unit =
  Util.renderGroups toScreen allTargets groups



[<EntryPoint>]
let main argv =
    Util.init();

    // run all test cases
    renderAll false;

    // To only run some test groups, use the following

    //renderGroups false ["light";"affineTransformationsSimple";"affineTransformationsCSG";"shapes";"texture";"meshes";"csg";]
    //renderGroups false ["csg"]

    // To only run some test cases of a group, use the following
    //renderTests false "texture" ["plane"]

    Util.finalize();
    0 // return an integer exit code