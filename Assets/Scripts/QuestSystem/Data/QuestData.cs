using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class QuestData
{
    [Header("< 퀘스트 기본 정보 >")]
    public string questId;
    public string questType;
    public bool isCleared;
    public bool isRewardGiven;

    [Header("< 레거시 물건별 보상 - 실제 지급 안 함 >")]
    [Tooltip("현재 게임에서는 물건별 코인을 지급하지 않고, 재스캔으로 한 라운드를 완료했을 때만 코인을 지급합니다.")]
    [Min(0)] public int rewardCredit = 0;

    [Header("< AI 탐지 원본 정보 >")]
    public int sourceObjectId;
    public int sourceClassId;
    public Rect sourceRect;
    [Range(0f, 1f)] public float confidence;
    public string suggestedAction;
    public bool userConfirmRequired;
}

[System.Serializable]
public class YOLOScanData
{
    public string scan_id;
    public string scan_type;
    public string area_type;
    public List<YOLOObjectData> candidate_objects;
    public YOLOScanSummary scan_summary;
}

[System.Serializable]
public class YOLOObjectData
{
    public string object_id;
    public int class_id;
    public string class_name;
    public float confidence;
    public BBox2D bbox_2d;
    public string suggested_action;
    public bool user_confirm_required;
}

[System.Serializable]
public class BBox2D
{
    public float x1;
    public float y1;
    public float x2;
    public float y2;
}

[System.Serializable]
public class YOLOScanSummary
{
    public int total_object_count;
    public List<ClassCountData> class_counts;
    public int tidiness_score;
    public bool needs_cleaning;
}

[System.Serializable]
public class ClassCountData
{
    public string class_name;
    public int count;
}
