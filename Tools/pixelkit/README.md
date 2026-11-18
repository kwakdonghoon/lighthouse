# pixelkit — 등대지기 도트 아이콘 작업 도구

`Tools/pixelkit/pixelkit.py` (Python 3 + Pillow). 아이콘 한 줄 정의 `"id;d=#2c3a4a,w=#c8d2e4,...;행|행|..."`(8×8·16×16)을
만들고 검사하고 눈으로 확인하는 일을 줄인다. 정의는 `RenderData.Generated.cs`(동결 표)와 `UiTheme.UnityIcons`(Unity 교체 그림)에서 읽는다.
교체 그림이 표보다 우선한다.

| 명령 | 하는 일 |
|---|---|
| `list` | 아이콘 id·크기·출처(표/Unity) |
| `lint [id…] [--sym id] [-v]` | 줄 수·길이·팔레트에 없는 글자 검사, `-v` 이면 외톨이 점·윤곽 비대칭 같은 참고 사항. 오류가 있으면 종료 코드 1 |
| `preview id… -o out.png [--zoom 16] [--bg #hex]` | 확대 미리보기(여러 개를 한 장에) |
| `from-png in.png --id x --like gear` | 8×8·16×16 PNG → 정의 한 줄. 팔레트는 `--like` 아이콘 것을 빌려 오고, 먼 색은 경고 |
| `mirror rows.txt --id x --like gear [--quad] [--hole]` | 윤곽(`X`/`.`)만 반쪽(또는 `--quad` 로 1/4)만 쓰면 대칭으로 펴고 테두리 `d`·명암 `h/w/s` 를 자동으로 칠한다 |
| `snap in.png out.png [색수]` | spritefusion-pixel-snapper 가 PATH 에 있으면 호출(없으면 안내) |

## 작업 흐름

1. **손으로 그릴 때**: 반쪽 윤곽을 `rows.txt` 에 쓰고 `mirror` → 나온 한 줄을 `UiTheme.UnityIcons` 에 붙인다(톱니가 이렇게 나왔다).
2. **AI 가 뽑은 그림을 쓸 때**: `snap` 으로 격자·팔레트에 맞춤 → 16×16 으로 1:1 줄임 → `from-png` → `UnityIcons` 에 붙인다.
3. 어느 쪽이든 마지막에 `lint` 와 `preview` 로 확인한다. 화면에서의 정렬은 `Tools/unity_run_icons.ps1` + `Tools/ui_center_pixel_audit.py` 가 본다.

## 조사 메모(외부 도구)

- **spritefusion-pixel-snapper**: AI 그림을 격자·팔레트에 맞추는 일에 맞다. 이 PC 에는 빌드된 실행 파일이 없다(`cargo install` 필요, Rust 설치 여부 미확인).
- **claude-pxl**: `.px.js` 로 도트를 코드로 그린다(`mx` 미러, `outline` 자동 테두리, `pxl ascii` 로 글자 격자 출력). 다만 `outline` 은 캔버스를 한 칸씩 키워 16×16 아이콘에는 맞지 않아(예: 16 → 18) 아이콘은 `mirror` 가 낫고, pxl 은 장면·애니메이션 시안용으로 보는 게 맞다.
- **sprite-gen / lemo-opuscar / mono-pix / Aseprite·LibreSprite·Pixelorama·PixiEditor**: 이번에 코드를 읽어 보지는 않았다. 아이콘 작업에는 위 둘로 충분해서 연결하지 않았다.
- 꾸미기 아이템(`decor.json`)은 팔레트가 따로라 이 도구의 `lint` 대상이 아니다(`Tools/g2_decor_gen.py` 가 원본).
