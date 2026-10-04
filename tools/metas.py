"""Give every asset under Assets/ a .meta (fresh GUID) if it has none, so every worktree's Editor imports the same GUIDs
instead of minting its own. Folders and scripts get their full default importer block; anything else just the GUID -
Unity fills in the importer defaults on import and keeps the GUID. Images get a full TextureImporter block too (a plain
2D texture, Unity 6's serializedVersion 13; normal maps as normal maps, data masks linear): a guid-only .meta for a texture
reads as an importer "at version 1, below the supported minimum" and imported the Tripo maps as Cubemaps, so materials
bound nothing (2026-10-04). Run before committing new assets made outside the Editor. Usage: python tools/metas.py [--dry]"""
import os, sys, uuid

# an imported prop texture's importer block (prop_amatsu_lantern_basecolor.jpg.meta), {srgb} / {ttype} filled per map
TEX = 'TextureImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 13\n  mipmaps:\n    mipMapMode: 0\n    enableMipMap: 1\n    sRGBTexture: {srgb}\n    linearTexture: 0\n    fadeOut: 0\n    borderMipMap: 0\n    mipMapsPreserveCoverage: 0\n    alphaTestReferenceValue: 0.5\n    mipMapFadeDistanceStart: 1\n    mipMapFadeDistanceEnd: 3\n  bumpmap:\n    convertToNormalMap: 0\n    externalNormalMap: 0\n    heightScale: 0.25\n    normalMapFilter: 0\n    flipGreenChannel: 0\n  isReadable: 0\n  streamingMipmaps: 0\n  streamingMipmapsPriority: 0\n  vTOnly: 0\n  ignoreMipmapLimit: 0\n  grayScaleToAlpha: 0\n  generateCubemap: 6\n  cubemapConvolution: 0\n  seamlessCubemap: 0\n  textureFormat: 1\n  maxTextureSize: 2048\n  textureSettings:\n    serializedVersion: 2\n    filterMode: 1\n    aniso: 1\n    mipBias: 0\n    wrapU: 0\n    wrapV: 0\n    wrapW: 0\n  nPOTScale: 1\n  lightmap: 0\n  compressionQuality: 50\n  spriteMode: 0\n  spriteExtrude: 1\n  spriteMeshType: 1\n  alignment: 0\n  spritePivot: {x: 0.5, y: 0.5}\n  spritePixelsToUnits: 100\n  spriteBorder: {x: 0, y: 0, z: 0, w: 0}\n  spriteGenerateFallbackPhysicsShape: 1\n  alphaUsage: 1\n  alphaIsTransparency: 0\n  spriteTessellationMethod: 0\n  spriteTessellationDetail: -1\n  spriteGeometrySubdivision: -1\n  textureType: {ttype}\n  textureShape: 1\n  singleChannelComponent: 0\n  flipbookRows: 1\n  flipbookColumns: 1\n  maxTextureSizeSet: 0\n  compressionQualitySet: 0\n  textureFormatSet: 0\n  ignorePngGamma: 0\n  applyGammaDecoding: 0\n  swizzle: 50462976\n  cookieLightType: 0\n  platformSettings:\n  - serializedVersion: 4\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 4096\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 2\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  - serializedVersion: 4\n    buildTarget: Standalone\n    maxTextureSize: 2048\n    resizeAlgorithm: 0\n    textureFormat: -1\n    textureCompression: 1\n    compressionQuality: 50\n    crunchedCompression: 0\n    allowsAlphaSplitting: 0\n    overridden: 0\n    ignorePlatformSupport: 0\n    androidETC2FallbackOverride: 0\n    forceMaximumCompressionQuality_BC6H_BC7: 0\n  spriteSheet:\n    serializedVersion: 2\n    sprites: []\n    outline: []\n    customData: \n    physicsShape: []\n    bones: []\n    spriteID: \n    internalID: 0\n    vertices: []\n    indices: \n    edges: []\n    weights: []\n    secondaryTextures: []\n    spriteCustomMetadata:\n      entries: []\n    nameFileIdTable: {}\n  mipmapLimitGroupName: \n  pSDRemoveMatte: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
IMAGES = ('.png', '.jpg', '.jpeg', '.tga')


def tex_meta(name):
    stem = os.path.splitext(name)[0].lower()
    normal = stem.endswith(('_normal', '_nrm', '_normalmap'))
    data = normal or stem.endswith(('_rm', '_urpmask', '_metallic', '_roughness', '_orm', '_ao', '_mask'))   # linear, not colour
    return TEX.replace('{srgb}', '0' if data else '1').replace('{ttype}', '1' if normal else '0')


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'Assets'))
dry = '--dry' in sys.argv
made = []
for dp, dns, fns in os.walk(ROOT):
    dns[:] = [d for d in dns if not d.startswith('.')]
    for name in dns + fns:
        if name.endswith('.meta') or name.startswith('.'):
            continue
        p = os.path.join(dp, name)
        if os.path.exists(p + '.meta'):
            continue
        body = 'fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n'
        if os.path.isdir(p):
            body += 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        elif name.endswith('.cs'):
            body += ('MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
                     '  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
        if not dry:
            with open(p + '.meta', 'w', encoding='utf-8', newline='\n') as f:
                f.write(body)
        made.append(os.path.relpath(p, os.path.dirname(ROOT)).replace(os.sep, '/'))
print(len(made), 'metas' + (' (dry run)' if dry else ' written'))
for m in made:
    print(' ', m)
