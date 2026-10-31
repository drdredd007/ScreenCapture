FFmpeg 64-bit static Windows build from www.gyan.dev

Version: 2026-10-29-git-31ba826726-essentials_build-www.gyan.dev

License: GPL v3

Source Code: https://github.com/FFmpeg/FFmpeg/commit/31ba826726

git-essentials build configuration: 

ARCH                      x86 (generic)
big-endian                no
runtime cpu detection     yes
standalone assembly       yes
x86 assembler             nasm
MMX enabled               yes
MMXEXT enabled            yes
SSE enabled               yes
SSSE3 enabled             yes
AESNI enabled             yes
CLMUL enabled             yes
AVX enabled               yes
AVX2 enabled              yes
AVX-512 enabled           yes
AVX-512ICL enabled        yes
XOP enabled               yes
FMA3 enabled              yes
FMA4 enabled              yes
i686 features enabled     yes
CMOV is fast              yes
EBX available             yes
6 registers available     yes
7 registers available     yes
debug symbols             yes
strip symbols             yes
optimize for size         no
optimizations             yes
static                    yes
shared                    no
network support           yes
threading support         pthreads
safe bitstream reader     yes
texi2html enabled         no
perl enabled              yes
pod2man enabled           yes
makeinfo enabled          yes
makeinfo supports HTML    yes
experimental features     yes
xmllint enabled           yes

External libraries:
avisynth                libopencore_amrnb       libwebp
bzlib                   libopencore_amrwb       libx264
cairo                   libopenjpeg             libx265
gmp                     libopenmpt              libxml2
gnutls                  libopus                 libxvid
iconv                   librubberband           libzimg
libaom                  libspeex                libzmq
libass                  libsrt                  lzma
libfontconfig           libssh                  mediafoundation
libfreetype             libtheora               openal
libfribidi              libvidstab              sdl2
libgme                  libvmaf                 vapoursynth
libgsm                  libvo_amrwbenc          zlib
libharfbuzz             libvorbis
libmp3lame              libvpx

External libraries providing hardware acceleration:
amf                     d3d12va                 nvdec
cuda                    dxva2                   nvenc
cuda_llvm               ffnvcodec               vaapi
cuvid                   libmfx
d3d11va                 libvpl

Libraries:
avcodec                 avformat                swscale
avdevice                avutil
avfilter                swresample

Programs:
ffmpeg                  ffplay                  ffprobe

Enabled decoders:
aac                     fits                    pcm_u32le
aac_fixed               flac                    pcm_u8
aac_latm                flashsv                 pcm_vidc
aasc                    flashsv2                pcx
ac3                     flic                    pdv
ac3_fixed               flv                     pfm
acelp_kelvin            fmvc                    pgm
adpcm_4xm               fourxm                  pgmyuv
adpcm_adx               fraps                   pgssub
adpcm_afc               frwu                    pgx
adpcm_agm               ftr                     phm
adpcm_aica              g2m                     photocd
adpcm_argo              g723_1                  pictor
adpcm_circus            g728                    pixlet
adpcm_ct                g729                    pjs
adpcm_dtk               gdv                     png
adpcm_ea                gem                     ppm
adpcm_ea_maxis_xa       gif                     prores
adpcm_ea_r1             gremlin_dpcm            prores_raw
adpcm_ea_r2             gsm                     prosumer
adpcm_ea_r3             gsm_ms                  psd
adpcm_ea_xas            h261                    ptx
adpcm_g722              h263                    qcelp
adpcm_g726              h263i                   qdm2
adpcm_g726le            h263p                   qdmc
adpcm_ima_acorn         h264                    qdraw
adpcm_ima_alp           h264_amf                qoa
adpcm_ima_amv           h264_cuvid              qoi
adpcm_ima_apc           h264_qsv                qpeg
adpcm_ima_apm           hap                     qtrle
adpcm_ima_citrix        hca                     r10k
adpcm_ima_cunning       hcom                    r210
adpcm_ima_dat4          hdr                     ra_144
adpcm_ima_dk3           hevc                    ra_288
adpcm_ima_dk4           hevc_amf                ralf
adpcm_ima_ea_eacs       hevc_cuvid              rasc
adpcm_ima_ea_sead       hevc_qsv                rawvideo
adpcm_ima_escape        hnm4_video              realtext
adpcm_ima_hvqm2         hq_hqa                  rka
adpcm_ima_hvqm4         hqx                     rl2
adpcm_ima_iss           huffyuv                 roq
adpcm_ima_magix         hymt                    roq_dpcm
adpcm_ima_moflex        iac                     rpza
adpcm_ima_mtf           idcin                   rscc
adpcm_ima_oki           idf                     rtv1
adpcm_ima_pda           iff_ilbm                rv10
adpcm_ima_qt            ilbc                    rv20
adpcm_ima_rad           imc                     rv30
adpcm_ima_smjpeg        imm4                    rv40
adpcm_ima_ssi           imm5                    rv60
adpcm_ima_wav           imm6                    rw2
adpcm_ima_ws            indeo2                  s302m
adpcm_ima_xbox          indeo3                  sami
adpcm_ms                indeo4                  sanm
adpcm_mtaf              indeo5                  sbc
adpcm_n64               interplay_acm           scpr
adpcm_psx               interplay_dpcm          screenpresso
adpcm_psxc              interplay_video         sdx2_dpcm
adpcm_rhetorex          ipu                     sga
adpcm_sanyo             jacosub                 sgi
adpcm_sbpro_2           jpeg2000                sgirle
adpcm_sbpro_3           jpegls                  sheervideo
adpcm_sbpro_4           jv                      shorten
adpcm_swf               kgv1                    simbiosis_imx
adpcm_thp               kmvc                    sipr
adpcm_thp_le            lagarith                siren
adpcm_vima              lead                    smackaud
adpcm_xa                libaom_av1              smacker
adpcm_xmd               libgsm                  smc
adpcm_yamaha            libgsm_ms               smvjpeg
adpcm_zork              libopencore_amrnb       snow
agm                     libopencore_amrwb       sol_dpcm
ahx                     libopus                 sp5x
aic                     libspeex                speedhq
alac                    libvorbis               speex
alias_pix               libvpx_vp8              srgc
als                     libvpx_vp9              srt
amrnb                   loco                    ssa
amrwb                   lscr                    stl
amv                     m101                    subrip
anm                     mace3                   subviewer
ansi                    mace6                   subviewer1
anull                   magicyuv                sunrast
apac                    mdec                    svq1
ape                     media100                svq3
apng                    metasound               tak
aptx                    microdvd                targa
aptx_hd                 mimic                   targa_y216
apv                     misc4                   tdsc
arbc                    mjpeg                   text
argo                    mjpeg_cuvid             theora
ass                     mjpeg_qsv               thp
asv1                    mjpegb                  tiertexseqvideo
asv2                    mlp                     tiff
atrac1                  mmvideo                 tmv
atrac3                  mobiclip                truehd
atrac3al                motionpixels            truemotion1
atrac3p                 movtext                 truemotion2
atrac3pal               mp1                     truemotion2rt
atrac9                  mp1float                truespeech
aura                    mp2                     tscc
aura2                   mp2float                tscc2
av1                     mp3                     tta
av1_amf                 mp3adu                  twinvq
av1_cuvid               mp3adufloat             txd
av1_qsv                 mp3float                ulti
avrn                    mp3on4                  utvideo
avrp                    mp3on4float             v210
avs                     mpc7                    v210x
avui                    mpc8                    vb
bethsoftvid             mpeg1_cuvid             vble
bfi                     mpeg1video              vbn
bink                    mpeg2_cuvid             vc1
binkaudio_dct           mpeg2_qsv               vc1_cuvid
binkaudio_rdft          mpeg2video              vc1_qsv
bintext                 mpeg4                   vc1image
bitpacked               mpeg4_cuvid             vcr1
bmp                     mpegvideo               vmdaudio
bmv_audio               mpl2                    vmdvideo
bmv_video               msa1                    vmix
bonk                    mscc                    vmnc
brender_pix             msmpeg4v1               vnull
c93                     msmpeg4v2               vorbis
cavs                    msmpeg4v3               vp3
cbd2_dpcm               msnsiren                vp4
ccaption                msp2                    vp5
cdgraphics              msrle                   vp6
cdtoons                 mss1                    vp6a
cdxl                    mss2                    vp6f
cfhd                    msvideo1                vp7
cinepak                 mszh                    vp8
clearvideo              mts2                    vp8_cuvid
cljr                    mv30                    vp8_qsv
cllc                    mvc1                    vp9
comfortnoise            mvc2                    vp9_amf
cook                    mvdv                    vp9_cuvid
cpia                    mvha                    vp9_qsv
cri                     mwsc                    vplayer
cscd                    mxpeg                   vqa
cyuv                    nellymoser              vqc
dca                     notchlc                 vvc
dds                     nuv                     vvc_qsv
derf_dpcm               on2avc                  wady_dpcm
dfa                     opus                    wavarc
dfpwm                   osq                     wavpack
dirac                   paf_audio               wbmp
dnxhd                   paf_video               wcmv
dolby_e                 pam                     webp
dpx                     pbm                     webp_anim
dsd_lsbf                pcm_alaw                webvtt
dsd_lsbf_planar         pcm_bluray              wmalossless
dsd_msbf                pcm_dvd                 wmapro
dsd_msbf_planar         pcm_dvda                wmav1
dsicinaudio             pcm_f16le               wmav2
dsicinvideo             pcm_f24le               wmavoice
dss_sp                  pcm_f32be               wmv1
dst                     pcm_f32le               wmv2
dvaudio                 pcm_f64be               wmv3
dvbsub                  pcm_f64le               wmv3image
dvdsub                  pcm_lxf                 wnv1
dvvideo                 pcm_mulaw               wrapped_avframe
dxa                     pcm_s16be               ws_snd1
dxtory                  pcm_s16be_planar        xan_dpcm
dxv                     pcm_s16le               xan_wc3
eac3                    pcm_s16le_planar        xan_wc4
eacmv                   pcm_s24be               xbin
eamad                   pcm_s24daud             xbm
eatgq                   pcm_s24le               xface
eatgv                   pcm_s24le_planar        xl
eatqi                   pcm_s32be               xma1
eightbps                pcm_s32le               xma2
eightsvx_exp            pcm_s32le_planar        xpm
eightsvx_fib            pcm_s64be               xsub
escape124               pcm_s64le               xwd
escape130               pcm_s8                  y41p
evrc                    pcm_s8_planar           ylc
exr                     pcm_sga                 yop
fastaudio               pcm_u16be               yuv4
ffv1                    pcm_u16le               zero12v
ffvhuff                 pcm_u24be               zerocodec
ffwavesynth             pcm_u24le               zlib
fic                     pcm_u32be               zmbv

Enabled encoders:
a64multi                h264_vaapi              pcm_s8
a64multi5               hdr                     pcm_s8_planar
aac                     hevc_amf                pcm_u16be
aac_mf                  hevc_d3d12va            pcm_u16le
ac3                     hevc_mf                 pcm_u24be
ac3_fixed               hevc_nvenc              pcm_u24le
ac3_mf                  hevc_qsv                pcm_u32be
adpcm_adx               hevc_vaapi              pcm_u32le
adpcm_argo              huffyuv                 pcm_u8
adpcm_g722              jpeg2000                pcm_vidc
adpcm_g726              jpegls                  pcx
adpcm_g726le            libaom_av1              pdv
adpcm_ima_alp           libgsm                  pfm
adpcm_ima_amv           libgsm_ms               pgm
adpcm_ima_apm           libmp3lame              pgmyuv
adpcm_ima_qt            libopencore_amrnb       phm
adpcm_ima_ssi           libopenjpeg             png
adpcm_ima_wav           libopus                 ppm
adpcm_ima_ws            libspeex                prores
adpcm_ms                libtheora               prores_aw
adpcm_swf               libvo_amrwbenc          prores_ks
adpcm_yamaha            libvorbis               qoi
alac                    libvpx_vp8              qtrle
alias_pix               libvpx_vp9              r10k
amv                     libwebp                 r210
anull                   libwebp_anim            ra_144
apng                    libx264                 rawvideo
aptx                    libx264rgb              roq
aptx_hd                 libx265                 roq_dpcm
ass                     libxvid                 rpza
asv1                    ljpeg                   rv10
asv2                    magicyuv                rv20
av1_amf                 mjpeg                   s302m
av1_d3d12va             mjpeg_qsv               sbc
av1_mf                  mjpeg_vaapi             sgi
av1_nvenc               mlp                     smc
av1_qsv                 movtext                 snow
av1_vaapi               mp2                     speedhq
avrp                    mp2fixed                srt
avui                    mp3_mf                  ssa
bitpacked               mpeg1video              subrip
bmp                     mpeg2_qsv               sunrast
cfhd                    mpeg2_vaapi             svq1
cinepak                 mpeg2video              targa
cljr                    mpeg4                   text
comfortnoise            msmpeg4v2               tiff
dca                     msmpeg4v3               truehd
dfpwm                   msrle                   tta
dnxhd                   msvideo1                ttml
dpx                     nellymoser              utvideo
dsd_msbf                opus                    v210
dvbsub                  pam                     vbn
dvdsub                  pbm                     vc2
dvvideo                 pcm_alaw                vnull
dxv                     pcm_bluray              vorbis
eac3                    pcm_dvd                 vp8_vaapi
exr                     pcm_f32be               vp9_qsv
ffv1                    pcm_f32le               vp9_vaapi
ffvhuff                 pcm_f64be               wavpack
fits                    pcm_f64le               wbmp
flac                    pcm_mulaw               webvtt
flashsv                 pcm_s16be               wmav1
flashsv2                pcm_s16be_planar        wmav2
flv                     pcm_s16le               wmv1
g723_1                  pcm_s16le_planar        wmv2
gif                     pcm_s24be               wrapped_avframe
h261                    pcm_s24daud             xbm
h263                    pcm_s24le               xface
h263p                   pcm_s24le_planar        xsub
h264_amf                pcm_s32be               xwd
h264_d3d12va            pcm_s32le               y41p
h264_mf                 pcm_s32le_planar        yuv4
h264_nvenc              pcm_s64be               zlib
h264_qsv                pcm_s64le               zmbv

Enabled hwaccels:
av1_d3d11va             hevc_vaapi              vc1_vaapi
av1_d3d11va2            mjpeg_nvdec             vp8_nvdec
av1_d3d12va             mjpeg_vaapi             vp8_nvdec_cuarray
av1_dxva2               mpeg1_nvdec             vp8_vaapi
av1_nvdec               mpeg1_nvdec_cuarray     vp9_d3d11va
av1_nvdec_cuarray       mpeg2_d3d11va           vp9_d3d11va2
av1_vaapi               mpeg2_d3d11va2          vp9_d3d12va
h263_vaapi              mpeg2_d3d12va           vp9_dxva2
h264_d3d11va            mpeg2_dxva2             vp9_nvdec
h264_d3d11va2           mpeg2_nvdec             vp9_nvdec_cuarray
h264_d3d12va            mpeg2_nvdec_cuarray     vp9_vaapi
h264_dxva2              mpeg2_vaapi             vvc_vaapi
h264_nvdec              mpeg4_nvdec             wmv3_d3d11va
h264_nvdec_cuarray      mpeg4_nvdec_cuarray     wmv3_d3d11va2
h264_vaapi              mpeg4_vaapi             wmv3_d3d12va
hevc_d3d11va            vc1_d3d11va             wmv3_dxva2
hevc_d3d11va2           vc1_d3d11va2            wmv3_nvdec
hevc_d3d12va            vc1_d3d12va             wmv3_nvdec_cuarray
hevc_dxva2              vc1_dxva2               wmv3_vaapi
hevc_nvdec              vc1_nvdec
hevc_nvdec_cuarray      vc1_nvdec_cuarray

Enabled parsers:
aac                     dvdsub                  mpegaudio
aac_latm                evc                     mpegvideo
ac3                     ffv1                    opus
adx                     flac                    png
ahx                     ftr                     pnm
amr                     g723_1                  prores
apv                     g729                    prores_raw
av1                     gif                     qoi
avs2                    gsm                     rv34
avs3                    h261                    rw2
bmp                     h263                    sbc
cavsvideo               h264                    sipr
cook                    hdr                     tak
cri                     hevc                    vc1
dca                     ipu                     vorbis
dirac                   jpeg2000                vp3
dnxhd                   jpegxl                  vp8
dnxuc                   jpegxs                  vp9
dolby_e                 lcevc                   vvc
dpx                     misc4                   webp
dvaudio                 mjpeg                   xbm
dvbsub                  mlp                     xma
dvd_nav                 mpeg4video              xwd

Enabled demuxers:
aa                      idf                     pcm_f64be
aac                     iff                     pcm_f64le
aax                     ifv                     pcm_mulaw
ac3                     ilbc                    pcm_s16be
ac4                     image2                  pcm_s16le
ace                     image2_alias_pix        pcm_s24be
acm                     image2_brender_pix      pcm_s24le
act                     image2pipe              pcm_s32be
adf                     image_bmp_pipe          pcm_s32le
adp                     image_cri_pipe          pcm_s8
ads                     image_dds_pipe          pcm_u16be
adx                     image_dpx_pipe          pcm_u16le
aea                     image_exr_pipe          pcm_u24be
afc                     image_gem_pipe          pcm_u24le
aiff                    image_gif_pipe          pcm_u32be
aix                     image_hdr_pipe          pcm_u32le
alp                     image_j2k_pipe          pcm_u8
amr                     image_jpeg_pipe         pcm_vidc
amrnb                   image_jpegls_pipe       pdv
amrwb                   image_jpegxl_pipe       pjs
anm                     image_jpegxs_pipe       pmp
apac                    image_pam_pipe          pp_bnk
apc                     image_pbm_pipe          pva
ape                     image_pcx_pipe          pvf
apm                     image_pfm_pipe          qcp
apng                    image_pgm_pipe          qoa
aptx                    image_pgmyuv_pipe       r3d
aptx_hd                 image_pgx_pipe          rawvideo
apv                     image_phm_pipe          rcwt
aqtitle                 image_photocd_pipe      realtext
argo_asf                image_pictor_pipe       redspark
argo_brp                image_png_pipe          rka
argo_cvg                image_ppm_pipe          rl2
asf                     image_psd_pipe          rm
ass                     image_qdraw_pipe        roq
ast                     image_qoi_pipe          rpl
astc                    image_rw2_pipe          rsd
au                      image_sgi_pipe          rso
av1                     image_sunrast_pipe      rtp
avi                     image_svg_pipe          rtsp
avisynth                image_tiff_pipe         s337m
avr                     image_vbn_pipe          sami
avs                     image_webp_pipe         sap
avs2                    image_xbm_pipe          sbc
avs3                    image_xpm_pipe          sbg
bethsoftvid             image_xwd_pipe          scc
bfi                     imf                     scd
bfstm                   ingenient               sdns
bink                    ipmovie                 sdp
binka                   ipu                     sdr2
bintext                 ircam                   sds
bit                     iss                     sdx
bitpacked               iv8                     segafilm
bmv                     ivf                     ser
boa                     ivr                     sga
bonk                    jacosub                 shorten
brstm                   jpeg_mpf                siff
c93                     jpegxl_anim             simbiosis_imx
caf                     jv                      sln
cavsvideo               ktx                     smacker
cdg                     kux                     smjpeg
cdxl                    kvag                    smush
cine                    laf                     sol
codec2                  lc3                     sox
codec2raw               libgme                  spdif
concat                  libopenmpt              srt
dash                    live_flv                stl
data                    lmlm4                   str
daud                    loas                    subviewer
dcstr                   lrc                     subviewer1
derf                    luodat                  sup
dfa                     lvf                     svag
dfpwm                   lxf                     svs
dhav                    m4v                     swf
dirac                   matroska                tak
dnxhd                   mca                     tedcaptions
dsf                     mcc                     thp
dsicin                  mgsts                   threedostr
dss                     microdvd                tiertexseq
dts                     mjpeg                   tmv
dtshd                   mjpeg_2000              truehd
dv                      mlp                     tta
dvbsub                  mlv                     tty
dvbtxt                  mm                      txd
dxa                     mmf                     ty
ea                      mods                    usm
ea_cdata                moflex                  v210
eac3                    mov                     v210x
epaf                    mp3                     vag
evc                     mpc                     vapoursynth
ffmetadata              mpc8                    vc1
filmstrip               mpegps                  vc1t
fits                    mpegts                  vividas
flac                    mpegtsraw               vivo
flic                    mpegvideo               vmd
flv                     mpjpeg                  vobsub
fourxm                  mpl2                    voc
frm                     mpsub                   vpk
fsb                     msf                     vplayer
fwse                    msnwc_tcp               vqf
g722                    msp                     vvc
g723_1                  mtaf                    w64
g726                    mtv                     wady
g726le                  musx                    wav
g728                    mv                      wavarc
g729                    mvi                     wc3
gdv                     mvr                     webm_dash_manifest
genh                    mxf                     webp_anim
gif                     mxg                     webvtt
gsm                     nc                      wsaud
gxf                     nistsphere              wsd
h261                    nsp                     wsvqa
h263                    nsv                     wtv
h264                    nut                     wv
hca                     nuv                     wve
hcom                    obu                     xa
hevc                    ogg                     xbin
hls                     oma                     xmd
hnm                     osq                     xmv
hxvs                    paf                     xvag
iamf                    pcm_alaw                xwma
ico                     pcm_f32be               yop
idcin                   pcm_f32le               yuv4mpegpipe

Enabled muxers:
a64                     h264                    pcm_s16le
ac3                     hash                    pcm_s24be
ac4                     hds                     pcm_s24le
adts                    hevc                    pcm_s32be
adx                     hls                     pcm_s32le
aea                     iamf                    pcm_s8
aiff                    ico                     pcm_u16be
alp                     ilbc                    pcm_u16le
amr                     image2                  pcm_u24be
amv                     image2pipe              pcm_u24le
apm                     ipod                    pcm_u32be
apng                    ircam                   pcm_u32le
aptx                    ismv                    pcm_u8
aptx_hd                 iterm2                  pcm_vidc
apv                     ivf                     pdv
argo_asf                jacosub                 psp
argo_cvg                jpeg_mpf                rawvideo
asf                     ktx                     rcwt
asf_stream              kvag                    rm
ass                     latm                    roq
ast                     lc3                     rso
astc                    lrc                     rtp
au                      m4v                     rtp_mpegts
avi                     matroska                rtsp
avif                    matroska_audio          sap
avm2                    mcc                     sbc
avs2                    md5                     scc
avs3                    microdvd                segafilm
bit                     mjpeg                   segment
caf                     mkvtimestamp_v2         smjpeg
cavsvideo               mlp                     smoothstreaming
codec2                  mmf                     sox
codec2raw               mov                     spdif
crc                     mp2                     spx
dash                    mp3                     srt
data                    mp4                     stream_segment
daud                    mpeg1system             streamhash
dfpwm                   mpeg1vcd                sup
dirac                   mpeg1video              swf
dnxhd                   mpeg2dvd                tee
dts                     mpeg2svcd               tg2
dv                      mpeg2video              tgp
eac3                    mpeg2vob                truehd
evc                     mpegts                  tta
f4v                     mpjpeg                  ttml
ffmetadata              mxf                     uncodedframecrc
fifo                    mxf_d10                 vc1
filmstrip               mxf_opatom              vc1t
fits                    null                    voc
flac                    nut                     vvc
flv                     obu                     w64
framecrc                oga                     wav
framehash               ogg                     webm
framemd5                ogv                     webm_chunk
g722                    oma                     webm_dash_manifest
g723_1                  opus                    webp
g726                    pcm_alaw                webvtt
g726le                  pcm_f32be               whip
gif                     pcm_f32le               wsaud
gsm                     pcm_f64be               wtv
gxf                     pcm_f64le               wv
h261                    pcm_mulaw               yuv4mpegpipe
h263                    pcm_s16be

Enabled protocols:
async                   httpproxy               rtmps
cache                   https                   rtmpt
concat                  icecast                 rtmpte
concatf                 ipfs_gateway            rtmpts
crypto                  ipns_gateway            rtp
data                    libsrt                  shared
dtls                    libssh                  srtp
fd                      libzmq                  subfile
ffrtmpcrypt             md5                     tcp
ffrtmphttp              mmsh                    tee
file                    mmst                    tls
ftp                     pipe                    udp
gopher                  prompeg                 udplite
gophers                 rtmp                    windows_handle
http                    rtmpe

Enabled filters:
a3dscope                dctdnoiz                pan
aap                     ddagrab                 perlin
abench                  deband                  perms
abitscope               deblock                 perspective
acompressor             decimate                phase
acontrast               deconvolve              photosensitivity
acopy                   dedot                   pixdesctest
acrossfade              deesser                 pixelize
acrossover              deflate                 pixscope
acrusher                deflicker               pp7
acue                    deinterlace_d3d12       premultiply
addroi                  deinterlace_qsv         premultiply_dynamic
adeclick                deinterlace_vaapi       prewitt
adeclip                 dejudder                procamp_vaapi
adecorrelate            delogo                  pseudocolor
adelay                  denoise_vaapi           psnr
adenorm                 deshake                 pullup
aderivative             despill                 qp
adrawgraph              detelecine              random
adrc                    dialoguenhance          readeia608
adynamicequalizer       dilation                readvitc
adynamicsmooth          displace                realtime
aecho                   doubleweave             remap
aemphasis               drawbox                 removegrain
aeval                   drawbox_vaapi           removelogo
aevalsrc                drawgraph               repeatfields
aexciter                drawgrid                replaygain
afade                   drawtext                reverse
afdelaysrc              drawvg                  rgbashift
afftdn                  drmeter                 rgbtestsrc
afftfilt                dynaudnorm              roberts
afir                    earwax                  rotate
afireqsrc               ebur128                 rubberband
afirsrc                 edgedetect              sab
aformat                 elbg                    scale
afreqshift              entropy                 scale2ref
afwtdn                  epx                     scale_cuda
agate                   eq                      scale_d3d11
agraphmonitor           equalizer               scale_d3d12
ahistogram              erosion                 scale_qsv
aiir                    estdif                  scale_vaapi
aintegral               exposure                scdet
ainterleave             extractplanes           scharr
alatency                extrastereo             scroll
alimiter                fade                    segment
allpass                 feedback                select
allrgb                  fftdnoiz                selectivecolor
allyuv                  fftfilt                 sendcmd
aloop                   field                   separatefields
alphaextract            fieldhint               setdar
alphamerge              fieldmatch              setfield
amerge                  fieldorder              setparams
ametadata               fillborders             setpts
amf_capture             find_rect               setrange
amix                    firequalizer            setsar
amovie                  flanger                 settb
amplify                 floodfill               sharpness_vaapi
amultiply               format                  shear
anequalizer             fps                     showcqt
anlmdn                  framepack               showcwt
anlmf                   framerate               showfreqs
anlms                   framestep               showinfo
anoisesrc               frc_amf                 showpalette
anull                   freezedetect            showspatial
anullsink               freezeframes            showspectrum
anullsrc                fspp                    showspectrumpic
apad                    fsync                   showvolume
aperms                  gainmap                 showwaves
aphasemeter             gblur                   showwavespic
aphaser                 geq                     shuffleframes
aphaseshift             gfxcapture              shufflepixels
apsnr                   gradfun                 shuffleplanes
apsyclip                gradients               sidechaincompress
apulsator               graphmonitor            sidechaingate
arealtime               grayworld               sidedata
aresample               greyedge                sierpinski
areverse                guided                  signalstats
arls                    haas                    signature
arnndn                  haldclut                silencedetect
asdr                    haldclutsrc             silenceremove
asegment                hdcd                    sinc
aselect                 headphone               sine
asendcmd                hflip                   siti
asetnsamples            highpass                smartblur
asetpts                 highshelf               smptebars
asetrate                hilbert                 smptehdbars
asettb                  histeq                  sobel
ashowinfo               histogram               spectrumsynth
asidedata               hqdn3d                  speechnorm
asisdr                  hqx                     split
asoftclip               hstack                  spp
aspectralstats          hstack_qsv              sr_amf
asplit                  hstack_vaapi            ssim
ass                     hsvhold                 ssim360
astats                  hsvkey                  stereo3d
astreamselect           hue                     stereotools
asubboost               huesaturation           stereowiden
asubcut                 hwdownload              streamselect
asupercut               hwmap                   subtitles
asuperpass              hwupload                super2xsai
asuperstop              hwupload_cuda           superequalizer
atadenoise              hysteresis              surround
atempo                  identity                swaprect
atilt                   idet                    swapuv
atrim                   il                      tblend
avectorscope            inflate                 telecine
avgblur                 interlace               testsrc
avsynctest              interleave              testsrc2
axcorrelate             join                    thistogram
azmq                    kerndeint               threshold
backgroundkey           kirsch                  thumbnail
bandpass                lagfun                  thumbnail_cuda
bandreject              latency                 tile
bass                    latticepal              tiltandshift
bbox                    lenscorrection          tiltshelf
bench                   libvmaf                 tinterlace
bilateral               life                    tlut2
bilateral_cuda          limitdiff               tmedian
biquad                  limiter                 tmidequalizer
bitplanenoise           loop                    tmix
blackdetect             loudnorm                tonemap
blackframe              lowpass                 tonemap_vaapi
blend                   lowshelf                tpad
blockdetect             lumakey                 transpose
blurdetect              lut                     transpose_cuda
bm3d                    lut1d                   transpose_vaapi
boxblur                 lut2                    treble
bwdif                   lut3d                   tremolo
bwdif_cuda              lutrgb                  trim
cas                     lutyuv                  unpremultiply
ccrepack                mandelbrot              unsharp
cellauto                maskedclamp             untile
channelmap              maskedmax               uspp
channelsplit            maskedmerge             v360
chorus                  maskedmin               vaguedenoiser
chromahold              maskedthreshold         varblur
chromakey               maskfun                 vectorscope
chromakey_cuda          mcdeint                 vflip
chromanr                mcompand                vfrdet
chromashift             median                  vibrance
ciescope                mergeplanes             vibrato
codecview               mestimate               vidstabdetect
color                   mestimate_d3d12         vidstabtransform
colorbalance            metadata                vif
colorchannelmixer       midequalizer            vignette
colorchart              minterpolate            virtualbass
colorcontrast           mix                     vmafmotion
colorcorrect            monochrome              volume
colordetect             morpho                  volumedetect
colorhold               movie                   vpp_amf
colorize                mpdecimate              vpp_qsv
colorkey                mptestsrc               vqe_amf
colorlevels             msad                    vstack
colormap                multiply                vstack_qsv
colormatrix             negate                  vstack_vaapi
colorspace              nlmeans                 w3fdif
colorspace_cuda         nnedi                   waveform
colorspectrum           noformat                weave
colortemperature        noise                   xbr
compand                 normalize               xcorrelate
compensationdelay       null                    xfade
concat                  nullsink                xmedian
convolution             nullsrc                 xpsnr
convolve                oscilloscope            xstack
copy                    overlay                 xstack_qsv
corr                    overlay_cuda            xstack_vaapi
cover_rect              overlay_qsv             yadif
crop                    overlay_vaapi           yadif_cuda
cropdetect              owdenoise               yaepblur
crossfeed               pad                     yuvtestsrc
crystalizer             pad_cuda                zmq
cue                     pad_vaapi               zoneplate
curves                  pal100bars              zoompan
datascope               pal75bars               zscale
dblur                   palettegen
dcshift                 paletteuse

Enabled bsfs:
aac_adtstoasc           h264_mp4toannexb        pgs_frame_merge
ahx_to_mp2              h264_redundant_pps      prores_metadata
apv_metadata            hapqa_extract           remove_extradata
av1_frame_merge         hevc_metadata           setts
av1_frame_split         hevc_mp4toannexb        showinfo
av1_metadata            imx_dump_header         side_data
chomp                   jpegapp                 smpte436m_to_eia608
dca_core                lcevc_merge             text2movsub
dovi_rpu                lcevc_metadata          trace_headers
dovi_split              media100_to_mjpegb      trim
dts2pts                 mjpeg2jpeg              truehd_core
dump_extradata          mjpega_dump_header      vp9_metadata
dv_error_marker         mov2textsub             vp9_raw_reorder
eac3_core               mpeg2_metadata          vp9_superframe
eia608_to_smpte436m     mpeg4_unpack_bframes    vp9_superframe_split
evc_frame_merge         noise                   vvc_metadata
extract_extradata       null                    vvc_mp4toannexb
filter_units            opus_metadata
h264_metadata           pcm_rechunk

Enabled indevs:
dshow                   lavfi                   vfwcap
gdigrab                 openal

Enabled outdevs:

git-essentials external libraries' versions: 

AMF                 v1.5.2-5-g57ee0cf
aom                 v3.15.2-117-g9ace39e02a
avisynth+           v3.7.5-619-gf176fd8d
cairo               1.18.6-4-g74755964e
ffnvcodec           n13.1.15.0-4-gf205307
fontconfig          2.18.3
freetype            VER-2-15-0
fribidi             v1.0.17-7-gd2f3e1f
game-music-emu      0.6.3-249-gf68963b
gnutls              3.8.13-3
gsm                 1.0.24-1
harfbuzz            15.0.0-2-g75d8ac5d
lame                3.100-3
libass              0.17.5-11-gf61db56
libssh              0.12.2-1
libunibreak         7.0-1
libvo-amrwbenc      v0.1.3-7-g884dd24
libvpl              v2.17.0-1-g674d015
libwebp             v1.6.0-362-g1844042
libxml2             v2.15.0-134-gc79c9612
ogg                 v1.3.6-7-g06a5e02
openal-soft         4a52288b77
opencore-amr        0.1.6-1
openjpeg2           2.5.4-2
openmpt             OpenMPT-1.32.11.00
opus                v1.6.1-85-ga8b2545e
rubberband          v4.0.0-2-ge4296ac
SDL                 release-2.32.0-261-g8acf7cfc0
speex               Speex-1.2.1-57-gf461ff4
srt                 v1.5.7-18-g9f23902
theora              v1.2.0alpha1-224-g28fd5ec
vaapi               2.11.0.pre1-281-gf052e984
vapoursynth         R81-7-gd2820111
vidstab             v1.1.0-291-ge2445c4
vmaf                v3.2.1-68-gf712fa75
vorbis              v1.3.7-37-g1b75110b
vpx                 v1.17.0-85-g900b2a52b
x264                0480cb05fa
x265                4.3-73-g5143a2a0
xvidcore            1.3.7-5
zeromq              4.3.5-5
zimg                release-3.0.6-253-g67e0603

