// Copyright (c) 2020 Takahiro Miyaura
// Released under the MIT license
// http://opensource.org/licenses/mit-license.php

using System.Runtime.InteropServices;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;
using Microsoft.MixedReality.Toolkit.Utilities;
using UnityEngine;

public class HandConstraintSourceDeteced : BaseInputHandler, IMixedRealitySourceStateHandler, IMixedRealitySourcePoseHandler
{

    private static string HANDMENU_CHILDREN_NAME = "Visuals";

    [SerializeField] private GameObject handMenu = null;
    [SerializeField] private GameObject handMenuContensController = null;
    [SerializeField] private Vector3 handMenuContensControllerOffset = Vector3.zero;

    [SerializeField, Range(0f, 1f)]
    private float markerDistance = 0.2f;

    private GameObject menuRaiseMarker;
    private PointerHandler handler;
    private Billboard billboard;
    private GameObject contentsController;

    #region MonoBehaviour Functions

    protected override void Start()
    {
        base.Start();
        
        //手を検出した場合にメニュー表示を行うマーカーの設定を行う。
        menuRaiseMarker = transform.GetChild(0).gameObject;
        menuRaiseMarker.SetActive(false);

        //マーカーをタップした時にメニューを表示するイベントを割り当てる。
        handler = menuRaiseMarker.GetComponent<PointerHandler>();
        handler.OnPointerClicked.AddListener(PointerHandler_OnPointerClicked);

        handMenu?.SetActive(false);

    }

    protected override void Update()
    {
        base.Update();
        if (!handMenu.activeSelf)
        {
            //メニュー非表示時にHandMenuに追加した不要なコンポーネントを廃棄
            if (billboard != null)
            {
                GameObject.DestroyImmediate(billboard);
            }

            if (contentsController != null)
            {
                GameObject.DestroyImmediate(contentsController);
            }
        }
    }

    #endregion

    #region IMixedRealitySourcePoseHandler Implementation

    public void OnSourcePoseChanged(SourcePoseEventData<TrackingState> eventData)
    {
    }

    public void OnSourcePoseChanged(SourcePoseEventData<Vector2> eventData)
    {
    }

    public void OnSourcePoseChanged(SourcePoseEventData<Vector3> eventData)
    {
        if (IsMenuOpened())
        { 
            menuRaiseMarker.transform.position = eventData.SourceData - (eventData.SourceData- Camera.main.transform.position).normalized * markerDistance;
            handler.enabled = true;
        }
    }

    #endregion

    #region IMixedRealitySourceStateHandler Implementation

    public void OnSourcePoseChanged(SourcePoseEventData<Quaternion> eventData)
    {
    }

    public void OnSourcePoseChanged(SourcePoseEventData<MixedRealityPose> eventData)
    {
    }

    public void OnSourceDetected(SourceStateEventData eventData)
    {
        if (IsMenuOpened())
        {
            menuRaiseMarker.SetActive(true);
        }
    }

    public void OnSourceLost(SourceStateEventData eventData)
    {
        menuRaiseMarker.SetActive(false);
        handler.enabled = false;
    }
    #endregion

    #region InputSystemGlobalHandlerListener Implementation

    /// <inheritdoc />
    protected override void RegisterHandlers()
    {
        CoreServices.InputSystem?.RegisterHandler<IMixedRealitySourceStateHandler>(this);
        CoreServices.InputSystem?.RegisterHandler<IMixedRealitySourcePoseHandler>(this);
    }

    /// <inheritdoc />
    protected override void UnregisterHandlers()
    {
        CoreServices.InputSystem?.UnregisterHandler<IMixedRealitySourceStateHandler>(this);
        CoreServices.InputSystem?.RegisterHandler<IMixedRealitySourcePoseHandler>(this);
    }

    #endregion InputSystemGlobalHandlerListener Implementation

    #region Utilites
    
    private bool IsMenuOpened()
    {
        return handMenu != null && !handMenu.activeSelf;
    }

    public void HandMenu_Closed()
    {
        handMenu.transform.Find(HANDMENU_CHILDREN_NAME)?.gameObject.SetActive(false);
        handMenu.SetActive(false);
    }

    private void PointerHandler_OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        handMenu.SetActive(true);
        var visuals = handMenu.transform.Find(HANDMENU_CHILDREN_NAME)?.gameObject;
        if (visuals != null)
        {
            visuals.SetActive(true);
            var controllerName = "ContentsController";
            contentsController = visuals.transform.Find(controllerName)?.gameObject;
            if (contentsController == null)
            {
                contentsController = GameObject.Instantiate(handMenuContensController);
                contentsController.name = controllerName;
                contentsController.transform.parent = visuals.transform;
                contentsController.transform.localPosition = handMenuContensControllerOffset;
                contentsController.transform.localRotation = Quaternion.Euler(0,0,0);
                var manipulationHandler = contentsController.transform.Find("Move")?.GetComponent<ManipulationHandler>();
                if (manipulationHandler != null)
                {
                    manipulationHandler.HostTransform = handMenu.transform;
                }

                var closeComponent = contentsController.transform.Find("Close");
                var pressableButtonHoloLens2 = closeComponent?.GetComponent<PressableButtonHoloLens2>();
                if (pressableButtonHoloLens2 != null)
                {
                    pressableButtonHoloLens2.ButtonPressed.AddListener(HandMenu_Closed);
                }

                var e = closeComponent?.GetComponent<Interactable>();
                if (e != null)
                {
                    e.OnClick.AddListener(HandMenu_Closed);
                }
            }

            handMenu.transform.position = menuRaiseMarker.transform.position;
            billboard = handMenu.EnsureComponent<Billboard>();

        }
        else
        {
            Debug.LogWarning("Not found the object is name of 'visuals' in Hand Menu children objects.");
        }

        menuRaiseMarker.SetActive(false);
    }
    
    #endregion
}