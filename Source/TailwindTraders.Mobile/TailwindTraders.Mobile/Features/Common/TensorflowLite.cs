﻿using Emgu.TF.Lite;
using System;
using System.Diagnostics;
using System.Linq;
using Xamarin.Forms;

namespace TailwindTraders.Mobile.Features.Common
{
    public static class TensorflowLite
    {
        private const string ImageFilename = "images/beagle.jpg";
        private const string LabelFilename = "pets/pets_labels_list.txt";
        private const string ModelFilename = "pets/detect.tflite";
        private const float MinScore = 0.4f;
        private const int NumThreads = 4;
        private const int ModelInputSize = 300;
        private const bool QuantizedModel = true;
        private const int LabelOffset = 1;

        private static bool initialized = false;
        private static string[] labels = null;
        private static FlatBufferModel model;
        private static IPlatformService platformService;
        private static bool useNumThreads;

        public static void DoNotStripMe()
        {
        }

        public static void Initialize()
        {
            if (initialized)
            {
                return;
            }

            platformService = DependencyService.Get<IPlatformService>();

            useNumThreads = Device.RuntimePlatform == Device.Android;

            var labelContent = platformService.GetContent(LabelFilename);
            labels = labelContent.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).ToArray();

            var modelFileName = platformService.CopyToFilesAndGetPath(ModelFilename);
            model = new FlatBufferModel(modelFileName);
            if (!model.CheckModelIdentifier())
            {
                throw new Exception("Model identifier check failed");
            }

            initialized = true;
        }

        public static void Work()
        {
            var imagePath = platformService.CopyToFilesAndGetPath(ImageFilename);

            Console.WriteLine($"Using image: {imagePath}");

            var watchInterpreter = Stopwatch.StartNew();

            var interpreter = new Interpreter(model, new BuildinOpResolver());
            if (useNumThreads)
            {
                interpreter.SetNumThreads(NumThreads);
            }

            var allocateTensorStatus = interpreter.AllocateTensors();
            if (allocateTensorStatus == Status.Error)
            {
                throw new Exception("Failed to allocate tensor");
            }

            watchInterpreter.Stop();

            Console.WriteLine($"Interpreter startup: {watchInterpreter.ElapsedMilliseconds}ms");

            var input = interpreter.GetInput();
            var inputTensor = interpreter.GetTensor(input[0]);
            platformService.ReadImageFileToTensor(
                imagePath, 
                QuantizedModel,
                inputTensor.DataPointer,
                ModelInputSize,
                ModelInputSize);

            var watchInvoke = Stopwatch.StartNew();
            interpreter.Invoke();
            watchInvoke.Stop();

            Console.WriteLine($"Interpreter invoke: {watchInvoke.ElapsedMilliseconds}ms");

            var output = interpreter.GetOutput();
            var outputIndex = output[0];

            var outputTensors = new Tensor[output.Length];
            for (var i = 0; i < output.Length; i++)
            {
                outputTensors[i] = interpreter.GetTensor(outputIndex + i);
            }

            var detection_boxes_out = outputTensors[0].GetData() as float[];
            var detection_classes_out = outputTensors[1].GetData() as float[];
            var detection_scores_out = outputTensors[2].GetData() as float[];
            var num_detections_out = outputTensors[3].GetData() as float[];

            var numDetections = num_detections_out[0];

            Console.WriteLine($"NumDetections: {numDetections}");

            for (int i = 0; i < numDetections; i++)
            {
                var score = detection_scores_out[i];
                var classId = (int)detection_classes_out[i];

                Console.WriteLine($"Found classId({classId}) with score({score})");

                if (classId >= 0 && classId < labels.Length)
                {
                    var label = labels[classId + LabelOffset];
                    if (score >= MinScore)
                    {
                        Console.WriteLine($"{label} with score greater than {MinScore}");
                    }
                }
            }
        }
    }
}
