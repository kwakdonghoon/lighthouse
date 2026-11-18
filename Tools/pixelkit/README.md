# PixelKit — 등대지기 도트 아이콘 도구 (Unity 에디터)

Unity 에디터 안에서 쓰는 도구다(Python·Node 같은 외부 실행 환경 없음). 코드는 `Assets/_Lighthouse/Scripts/Editor/PixelKit.cs`,
시험은 `Assets/_Lighthouse/Tests/UI/Unity/PixelKitTests.cs`.

아이콘 한 줄 정의 `"id;d=#2c3a4a,w=#c8d2e4,...;행|행|..."`(8×8·16×16)을 읽는 곳은 `IconBaker`(동결 표)와 `UiTheme.UnityIcons`(Unity 교체 그림)이고,
교체 그림이 표보다 우선한다. 새로 만든 한 줄은 `UiTheme.UnityIcons` 에 붙이면 화면에 반영된다.

## 창: 메뉴 `Lighthouse ▸ PixelKit`

| 구역 | 하는 일 |
|---|---|
| 아이콘 미리보기·검사 | 아이콘을 골라 확대해서 본다. 오류(줄 수·길이·팔레트에 없는 글자)와 참고 사항(안 쓴 글자·외톨이 점·윤곽 비대칭)을 보여 주고, 정의 한 줄을 복사한다. "전체 검사"는 모든 아이콘을 검사해 콘솔에 남긴다 |
| 새 아이콘 ① 반쪽 윤곽 | `X`(채움)/`.`(투명)로 반쪽(또는 1/4)만 쓰면 대칭으로 펴고, 바깥 테두리 `d`와 안쪽 명암 `h`·`w`·`s`를 자동으로 칠한다. 톱니가 이렇게 나왔다 |
| 새 아이콘 ② PNG | 8×8·16×16 PNG 에셋을 정의 한 줄로 바꾼다. 팔레트는 고른 아이콘 것을 빌려 오고, 팔레트와 먼 색은 경고한다 |

## 시험

- `EveryIconDefinitionPassesLint`: 모든 아이콘 정의가 모양·팔레트 검사를 통과.
- `GearOverrideIsSymmetricAndMatchesTheMirrorGenerator`: 톱니가 대칭이고 `mirror` 결과와 같다.
- `RenderThenFromTextureRoundTrips`·`FromTextureRejectsWrongSizes`: 그리기→되읽기가 서로 맞고, 크기가 틀리면 거부.

## 한계

- 큰 그림(AI 가 뽑은 이미지)을 격자에 맞추는 기능은 없다. 먼저 격자·팔레트에 맞춰 8×8·16×16 으로 만든 PNG 만 받는다.
- 꾸미기 아이템(`decor.json`)은 팔레트가 따로라 대상이 아니다(`Tools/g2_decor_gen.py` 가 원본).
- 화면에서의 정렬은 `Tools/unity_run_icons.ps1` 캡처 + `Tools/ui_center_pixel_audit.py`·`ButtonCenterRegressionTests` 가 본다.
