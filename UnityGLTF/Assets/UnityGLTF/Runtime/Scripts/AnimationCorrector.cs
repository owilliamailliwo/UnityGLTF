using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityGLTF;
using GLTF.Schema;


public class AnimationCorrector
{
	public static string _dealtWithBoneName = "root";
	private string _dealtWithCurveBindingsPath;

	public static string _footName = "foot";
	private static string _leftFootName = _footName + "_L";
	private static string _rightFootName = _footName + "_R";

	private string _leftFootCurveBindingsPath;
	private List<EditorCurveBinding> _leftFootCurveBindings;
	private string _rightFootCurveBindingsPath;
	private List<EditorCurveBinding> _rightFootCurveBindings;

	private List<GLTFSceneExporter.CurveBindingGroup> _leftFootParentCurveBindings = new List<GLTFSceneExporter.CurveBindingGroup>();
	private List<GLTFSceneExporter.CurveBindingGroup> _rightFootParentCurveBindings = new List<GLTFSceneExporter.CurveBindingGroup>();

	private Vector3 _initialPoint = Vector3.zero;


	public void Init(List<GLTFSceneExporter.CurveBindingGroup> curveBindingGroups, Transform rootNodeTransform)
	{
		Transform parentBone = null;
		Transform leftFootBone = null;
		Transform rightFootBone = null;

		Dictionary<string, GLTFSceneExporter.CurveBindingGroup> curveBindingGroupsDict = new Dictionary<string, GLTFSceneExporter.CurveBindingGroup>();

		foreach (var curveBindingGroup in curveBindingGroups)
		{
			curveBindingGroupsDict.Add(curveBindingGroup.path, curveBindingGroup);

			var bone = rootNodeTransform.Find(curveBindingGroup.path);

			if (bone.name == _leftFootName)
			{
				foreach (var property in curveBindingGroup.properties)
				{
					if ("m_LocalPosition" == property.name)
					{
						_leftFootCurveBindings = property.curveBindings;
					}
				}

				_leftFootCurveBindingsPath = curveBindingGroup.path;
				leftFootBone = bone;
			}

			if (bone.name == _rightFootName)
			{
				foreach (var property in curveBindingGroup.properties)
				{
					if ("m_LocalPosition" == property.name)
					{
						_rightFootCurveBindings = property.curveBindings;
					}
				}

				_rightFootCurveBindingsPath = curveBindingGroup.path;
				rightFootBone = bone;
			}

			if (bone.name == _dealtWithBoneName)
			{
				_dealtWithCurveBindingsPath = curveBindingGroup.path;
				parentBone = bone.parent;
			}
		}

		if (null == leftFootBone || null == rightFootBone || null == parentBone)
		{
			Debug.LogError("Invalid skeleton structure.");
			return;
		}

		var leftFoot = parentBone.transform.worldToLocalMatrix.MultiplyPoint3x4(leftFootBone.position);
		var rightFoot = parentBone.transform.worldToLocalMatrix.MultiplyPoint3x4(rightFootBone.position);

		_initialPoint = (leftFoot + rightFoot) / 2.0f;

		GetAllParents(_leftFootCurveBindingsPath, ref _leftFootParentCurveBindings, curveBindingGroupsDict);
		GetAllParents(_rightFootCurveBindingsPath, ref _rightFootParentCurveBindings, curveBindingGroupsDict);
	}

	public Vector3[] Corrector(AnimationClip animationClip,
		string propertyName,
		List<EditorCurveBinding> curveBindings,
		int frameCount,
		string curveBindingGroupPath,
		out GLTFAnimationChannelPath path)
	{
		if(curveBindingGroupPath != _dealtWithCurveBindingsPath)
		{
			path = GLTFAnimationChannelPath.translation;
			return null;
		}

		switch (propertyName)
		{
			case "m_LocalPosition":
				{
					var data = new Vector3[frameCount];
					for (var i = 0; i < frameCount; ++i)
					{
						var time = i / animationClip.frameRate;

						var curveIndex = 0;
						var value = new float[3];
						foreach (var curveBinding in curveBindings)
						{
							var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
							if (curve != null) value[curveIndex++] = curve.Evaluate(time);
						}

						var frameData = new Vector3(value[0], value[1], value[2]);

						Vector3 left = Vector3.zero;
						Vector3 right = Vector3.zero;
						GetPostionInParentSpace(time, animationClip, _leftFootCurveBindings, _leftFootParentCurveBindings, ref left);
						GetPostionInParentSpace(time, animationClip, _rightFootCurveBindings, _rightFootParentCurveBindings, ref right);

						Vector3 newPoint = (left + right) / 2.0f;
						Vector3 offset = newPoint - _initialPoint;

						data[i] = frameData - offset;
					}

					path = GLTFAnimationChannelPath.translation;
					return data;
				}
			case "m_LocalRotation":
				{
					path = GLTFAnimationChannelPath.rotation;
					return null;
				}
			case "m_LocalScale":
				{
					path = GLTFAnimationChannelPath.scale;
					return null;
				}
			case "localEulerAnglesRaw":
				{
					throw new Exception("Parsing of localEulerAnglesRaw is not supported.");
				}
		}

		Debug.LogError("Unrecognized property name: " + propertyName);
		path = GLTFAnimationChannelPath.translation;
		return null;
	}

	private void GetPostionInParentSpace(float time,
		AnimationClip animationClip,
		List<EditorCurveBinding> footCurveBindings,
		List<GLTFSceneExporter.CurveBindingGroup> footParentCurveBindings,
		ref Vector3 footPos)
	{
		var curveIndex = 0;
		var value = new float[3];
		foreach (var curveBinding in footCurveBindings)
		{
			var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
			if (curve != null) value[curveIndex++] = curve.Evaluate(time);
		}

		footPos = new Vector3(value[0], value[1], value[2]);

		foreach (var curveBindingGroup in footParentCurveBindings)
		{
			Vector3 translation = Vector3.zero;
			Quaternion quaternion = Quaternion.identity;
			Vector3 scale = Vector3.one;

			foreach (var property in curveBindingGroup.properties)
			{
				switch (property.name)
				{
					case "m_LocalPosition":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							translation = new Vector3(v[0], v[1], v[2]);
							break;
						}
					case "m_LocalRotation":
						{
							var ci = 0;
							var v = new float[4];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							quaternion = new Quaternion(v[0], v[1], v[2], v[3]);
							break;
						}
					case "m_LocalScale":
						{
							var ci = 0;
							var v = new float[3];
							foreach (var curveBinding in property.curveBindings)
							{
								var curve = AnimationUtility.GetEditorCurve(animationClip, curveBinding);
								if (curve != null) v[ci++] = curve.Evaluate(time);
							}

							scale = new Vector3(v[0], v[1], v[2]);
							break;
						}
				}
			}

			Matrix4x4 m = Matrix4x4.TRS(translation, quaternion.normalized, scale);
			footPos = m.MultiplyPoint3x4(footPos);
		}
	}

	private void GetAllParents(string footCurveBindingsPath,
		ref List<GLTFSceneExporter.CurveBindingGroup> footParentCurveBindings,
		Dictionary<string, GLTFSceneExporter.CurveBindingGroup> curveBindingGroups)
	{
		var path = footCurveBindingsPath;
		var removeStart = path.Length;
		while (removeStart != -1)
		{
			if (path == _dealtWithCurveBindingsPath)
			{
				break;
			}

			removeStart = path.LastIndexOf("/");
			if (removeStart != -1)
			{
				path = path.Remove(removeStart);

				GLTFSceneExporter.CurveBindingGroup parent;
				if (curveBindingGroups.TryGetValue(path, out parent))
				{
					footParentCurveBindings.Add(parent);
				}
			}
		}
	}
}
