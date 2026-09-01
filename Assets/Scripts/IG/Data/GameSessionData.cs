using System;

namespace IGMain
{
    [Serializable]
    public class BoardTileSaveData
    {
        public string value;
    }

    [Serializable]
    public class PendingBlockSaveData
    {
        public int shapeType;
        public string[] tileValues;
    }

    [Serializable]
    public class GameSessionData
    {
        public bool hasActiveSession;
        public long currentScore;
        public int comboCount;
        public BoardTileSaveData[] boardTiles;
        public PendingBlockSaveData[] pendingBlocks;

        /// <summary>
        /// 이 판에서 이미 부활을 썼는지. 부활은 판당 1회다.
        ///
        /// 예전에는 IGGameController의 메모리 변수로만 있어서, 앱을 다시 켜면 초기화됐다.
        /// 세션은 복원되는데 부활 횟수만 리셋되니 광고 한 번으로 무제한 부활이 가능했다.
        /// </summary>
        public bool hasRevived;

        /// <summary>
        /// 이 세션 파일의 일련번호. 되감기(replay) 방어용이며 SaveManager가 채운다.
        ///
        /// 서명은 "이 앱이 썼는가"만 보증하고 **신선도는 보증하지 않는다.** 즉 파일을 고치는 것은
        /// 막지만 예전 파일을 그대로 되돌리는 것은 막지 못한다. 부활 전(hasRevived=false)
        /// 세션을 adb pull 로 떠 두었다가 광고를 보고 부활한 뒤 되밀면, 앱은 부활을 쓰지 않은
        /// 것으로 보고 또 부활을 준다 — 보상형 광고 없이 무한 부활이다.
        /// 유리한 보드 상태를 떠 두고 실수할 때마다 되돌리는 세이브 스커밍도 같은 원리다.
        ///
        /// 그래서 저장할 때마다 번호를 올려 찍고, 로드할 때 내부 저장소(PlayerPrefs)에 남긴
        /// 기대값보다 낮으면 오래된 파일로 보고 버린다. 번호는 서명 대상(payload)에 들어 있어
        /// 위조할 수 없고, 기대값은 외부 저장소가 아니라 내부 저장소에 있어 낮출 수 없다.
        ///
        /// 이 필드가 없던 구버전 세션은 0으로 읽히는데, 기대값도 0에서 시작하므로 한 번은
        /// 정상 복원된다(업데이트 유저가 진행 중이던 판을 잃지 않는다).
        /// </summary>
        public int serial;
    }
}
