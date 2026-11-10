param([string]$Root,[string]$NativeRoot=(Join-Path $Root 'out/native'))
$ErrorActionPreference='Stop'
function Replace-Once([string]$source,[string]$old,[string]$new){
    if(([regex]::Matches($source,[regex]::Escape($old))).Count -ne 1){throw "Pinned texture hook changed: $old"}
    return $source.Replace($old,$new)
}
Copy-Item (Join-Path $Root 'tools/native-texture-stream.h') (Join-Path $NativeRoot 'include/native-texture-stream.h') -Force
Copy-Item (Join-Path $Root 'tools/native-texture-cache.inc') (Join-Path $NativeRoot 'source/native-texture-cache.inc') -Force
Copy-Item (Join-Path $Root 'tools/native-texture-stream.c') (Join-Path $NativeRoot 'source/animaltown_texture_stream.c') -Force
& python3 (Join-Path $Root 'tools/patch-texture-model.py') $NativeRoot
if($LASTEXITCODE){throw 'External texture loader patch failed'}
$graphicsPath=Join-Path $NativeRoot 'source/model_graphics.c'
if(Test-Path $graphicsPath){
    $graphics=[IO.File]::ReadAllText($graphicsPath)
    $graphics="#include `"native-texture-stream.h`"`n"+$graphics
    $graphics=Replace-Once $graphics '      DndTexture *tx = &model->texture[mat->texture];' @'
      DndTexture *tx = &model->texture[mat->texture];
      DndTexture town_view;int town_format;
      if(!town_texture_view(tx,&town_view,&town_format))return -1;
      tx=&town_view;
'@
    $graphics=Replace-Once $graphics '                    GX_TF_RGBA8, (u8)tx->wrap_s, (u8)tx->wrap_t,' '                    (u8)town_format, (u8)tx->wrap_s, (u8)tx->wrap_t,'
    $graphics=Replace-Once $graphics 'if(!reuse){DndTexture *tx=&model->texture[mat->secondary];GXTexObj tex;' @'
if(!reuse){DndTexture *tx=&model->texture[mat->secondary];GXTexObj tex;
      DndTexture town_view;int town_format;if(!town_texture_view(tx,&town_view,&town_format))return -1;tx=&town_view;
'@
    $graphics=Replace-Once $graphics 'GX_TF_RGBA8,(u8)tx->wrap_s,(u8)tx->wrap_t,tx->levels>1);' '(u8)town_format,(u8)tx->wrap_s,(u8)tx->wrap_t,tx->levels>1);'
    $graphics+="`nvoid town_model_texture_reset(void){batch_model=NULL;batch_material=NULL;}`n"
    [IO.File]::WriteAllText($graphicsPath,$graphics)
}
$assetPath=Join-Path $NativeRoot 'source/animaltown_asset_residency.c'
if(Test-Path $assetPath){[IO.File]::WriteAllText($assetPath,"#define ANIMALTOWN_TEXTURE_STREAM 1`n"+[IO.File]::ReadAllText($assetPath))}
$generatedPath=Join-Path $NativeRoot 'source/generated_program.c'
if(Test-Path $generatedPath){
    $generated=[IO.File]::ReadAllText($generatedPath)
    $bridges=@{Init=1;Begin=1;Step=1;Read=0;Complete=1;Stats=1;Flags=1;HasModel=1;Demand=3;GeometryScope=1;PoseRegister=3;PoseActor=1;PoseBatch=1}
    foreach($method in $bridges.Keys){
        $n=$bridges[$method];$parameters=if($n){((1..$n|ForEach-Object {'intptr_t (\w+)'}) -join ', ')}else{'void'}
        $pattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeTextures_'+$method+'_[a-z0-9]+)\('+$parameters+'\) \{.*?\r?\n\}'
        if(([regex]::Matches($generated,$pattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw "Pinned texture bridge changed: $method"}
        $operation=$method.ToLowerInvariant()
        $generated=[regex]::Replace($generated,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($hit)
            $arguments=@();for($i=0;$i -lt $n;$i++){$cast=if($method -eq 'Init'){'(DndArray*)'}elseif($method -eq 'HasModel'){'(DndString*)'}else{'(int)'};$arguments+=$cast+$hit.Groups[$i+2].Value}
            'intptr_t '+$hit.Groups[1].Value+'('+$(if($n){((0..($n-1)|ForEach-Object {'intptr_t '+$hit.Groups[$_+2].Value}) -join ', ')}else{'void'})+') { return animaltown_textures_'+$operation+'('+($arguments -join ',')+'); }'
        },[Text.RegularExpressions.RegexOptions]::Singleline)
    }
    $generated="#include `"native-texture-stream.h`"`n"+$generated
    [IO.File]::WriteAllText($generatedPath,$generated)
}
