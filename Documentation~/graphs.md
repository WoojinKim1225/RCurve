# 문서 그래프: 입력, 데이터, 재생성

README와 사용 가이드의 그래프는 임의로 그린 예상 결과가 아니라 **현재 RCurve 코드의
관리형 평가 결과**에서 생성한 SVG다. 별도 PolynomialIndex 연구 결과를 가져오지 않는다.
그래프는 0.3.0 코드로 생성했다. WebGL 데모는 별도의 입력과 fitting 설정을 사용하므로 이 그래프와 같은 실험은 아니다.

- [원본과 bake 비교](images/curve-accuracy.svg)
- [Magic의 인덱스 예측·구간 보정·값 평가](images/magic-lookup.svg)
- [원시 데이터 JSON](images/data.json)
- [생성 코드](tools/RCurveDocumentationGraphs.cs)

## 1. Weighted curve와 오차

Unity 6000.5.8f1, `Minimax / High (5/5) / Auto`, 값 허용 오차 0.001,
기울기 허용 오차 0.01, 최대 분할 깊이 8. Pre/Post wrap은 ClampForever다.

| 키 | 시간 | 값 | 구간에 쓰이는 접선 | 활성 weight |
|---|---:|---:|---:|---:|
| 시작 | 0 | 0 | outTangent = 3 | outWeight = 0.15 |
| 끝 | 1 | 1 | inTangent = -0.7 | inWeight = 0.65 |

513개 시각을 두 evaluator에 **동일한 float 값**으로 전달했다. 에디터 화면을 캡처한 것이
아니라 `AnimationCurve.Evaluate`와 `RationalCurve.Evaluate`를 직접 호출했다.
위쪽 그래프는 실제 값을 같은 축으로 표시하고, 아래쪽은 `RCurve - Unity` 오차만 별도
축으로 표시한다. 세로 점선은 bake 결과의 구간 경계다. 허용 오차선처럼 보이면 안 된다.

이 실행에서 최대 절대 샘플 오차는 약 `1.734821e-6`이었다. 이 값에는 fitting 오차와
Unity의 float 평가 차이가 함께 포함된다. 예를 들어 시작 키 값은 정확히 0이지만 이
weighted 예시에서 Unity의 시작 시각 평가값은 약 `2.011657e-8`이었다. RCurve가 보존하는
것은 입력 키 값과 구간의 유한 접선이지 Unity Evaluate와의 모든 비트 일치가 아니다.

이 그림 하나로 모든 커브의 정확도·연속 오차 상한·성능을 추정하면 안 된다.
플롯은 샘플을 선으로 연결하며, 샘플 사이를 추가로 검증하지 않는다.

## 2. Magic의 질의 처리 순서

성공/실패 분포 비교 대신 **한 질의가 실제 구간을 찾는 과정**을 보여준다.
Magic은 `time → animation value`를 직접 근사하는 함수가 아니라,
`time → key index` 예측기로 후보 구간을 찾는 탐색 전략이다.

예시 입력은 다음과 같다. 모든 키의 in/out tangent는 0, weight는 비활성,
Pre/Post wrap은 ClampForever다. 기본 fitting 설정에 `CurveLookupMode.Magic`을 명시했다.

| 키 인덱스 | 시간 | 커브 값 |
|---|---:|---:|
| 0 | 0 | 0 |
| 1 | 0.69 | 0.8 |
| 2 | 0.72 | 0.2 |
| 3 | 0.91 | 1.3 |
| 4 | 1 | 1 |

예측기의 키는 bake 결과의 구간 경계이며, 일반적으로 fitting 중 분할로 추가된 경계도 포함한다.
이 예시에서는 추가 분할 없이 구간이 4개이고, 선형 인덱스 예측기가 선택된다.
실제 저장된 예측기는 `f(u)≈3.5915u−0.5348`이다. 그림에서는 다항식 그래프를 생략하고
그 출력과 후보 구간만 표시한다.
`u=(mappedTime−firstTime)/(lastTime−firstTime)`이며 이 예시에서는 범위가 `[0,1]`이라
`u=t`다. 원본 키는 float이고, 그림과 표는 읽기 쉽게 반올림했다. JSON에 전체 숫자를 보관한다.

질의 `t=0.95`의 단계:

1. **예측:** `f(0.95)≈2.877`. 이것은 커브 값이 아닌 연속적인 인덱스 추정값이다.
2. **후보 선택:** `clamp(floor(2.877), 0, 3)=2`이므로 구간 2가 후보가 된다.
   구현에서는 음수가 되지 않게 clamp한 뒤 int로 변환하며, 이 범위에서는 floor와 같다.
3. **경계 비교/보정:** 후보 구간은 `[times[2],times[3])≈[0.72,0.91)`이다.
   `0.95≥times[3]`이므로 한 칸 오른쪽인 구간 3을 확인한다.
4. **실제 평가:** `[times[3],times[4])≈[0.91,1)`에 들어가므로
   `segment[3].Evaluate(0.95)`를 평가한다. 반환 값은 약 `1.175`로,
   예측 인덱스 `2.877`이나 구간 번호 `3`과 다르다.

일반적으로 시각이 후보 구간 왼쪽이면 한 칸 왼쪽, 오른쪽이면 한 칸 오른쪽으로 보정하고,
내부면 그대로 둔다. 정확한 키에서는 저장된 키 값을 반환한다. 보정한 구간도 확인에
실패하면 binary search로 돌아간다. Bake 시 예측기 검증 실패 역시 binary search를 선택한다.
그림은 **하나의 시간축을 `0.60–1.00`으로 확대**한다. 칸 너비는 실제 구간 길이에
비례하며 왼쪽 구간 0은 일부만 보인다. 주황색은 예측한 후보, 초록색은 실제 질의를
포함하는 구간, 보라색은 같은 질의 시각이다. 화살표는 구간 선택의 이동이지 시간의
이동이 아니다. 수식·값 계산·fallback 세부 사항은 그림이 아닌 이 본문에서 설명한다.

생성기는 모든 키 및 513개 샘플 시각의 **관리형 평가**를 독립적인 binary-search 커브와
비교한다. 질의 trace에서는 `intervalHint=-1`로 시작해 hint 적중으로 Magic을 우회하지
않도록 하고, `Evaluate(time, ref hint)`가 돌려준 실제 구간이 3인지 확인한다. 후보가 2이고
오른쪽 경계를 넘었다는 조건과 해당 rational segment의 평가값 일치도 검사한다.
Burst는 전략 선택만 확인하고 Job은 실행하지 않는다. Burst 실행 검증은 기존 테스트의 역할이다.

## 3. 재생성

Unity 프로젝트에 열려 있는 씬을 변경하지 않도록 **별도의 테스트 호스트**를 사용한다.
이 호스트에는 현재 로컬 RCurve 작업 사본이 UPM으로 설치되어 있어야 한다.
`Documentation~`는 Unity가 자동 import하지 않으므로 생성 코드를 명시적으로 복사한다.

다음 명령은 RCurve 저장소 루트에서 실행한다. 현재 열려 있는 Unity 프로젝트에는
실행하지 말고, 같은 클래스의 이전 복사본을 중복으로 넣지 않는다.

```sh
HOST=/path/to/isolated-unity-project
UNITY=/path/to/Unity
mkdir -p "$HOST/Assets/Editor/RCurveDocumentation"
cp 'Documentation~/tools/RCurveDocumentationGraphs.cs' "$HOST/Assets/Editor/RCurveDocumentation/"
RCURVE_GRAPH_OUTPUT="$PWD/Documentation~/images" "$UNITY" \
  -batchmode -nographics -projectPath "$HOST" \
  -executeMethod RCurveDocumentationGraphs.Generate -quit \
  -logFile /tmp/rcurve-doc-graphs.log
```

생성 시 키 값·구간 접선 보존, 샘플 오차 허용 범위, Magic의 후보·실제 보정 구간·평가값,
binary-search 결과 일치를 검사한다. 검사에 실패하면 예외를 내고 새로운 그래프를
기록하지 않는다. 성공하면 두 SVG와 `data.json`을 갱신한다. 씬이나 package runtime
코드는 수정하지 않는다. 새 차트 라이브러리나 Python 의존성은 필요 없다.

SVG는 자체 흰 배경, 축 단위, 범례, title/description을 포함한다. 원본/근사 선은
색뿐 아니라 실선/점선으로 구분한다. GitHub README에서는 상대 경로로 렌더링된다.

## 4. 성능 그래프를 넣지 않은 이유

현재 그림은 정확도와 동작 설명용이다. 별도 연구 저장소의 PGM/RadixSpline 결과나
WebGL UI 프레임 시간을 RCurve 성능 수치로 전용하지 않는다. 성능 그래프를 추가한다면
[테스트 안내](testing.md)의 동일 입력·실제 Burst 실행 벤치마크에서 얻은 결과와
호스트·설정·실행 조건을 함께 제시해야 한다.
