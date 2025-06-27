from PIL import ImageDraw,Image,ImageOps
import numpy as np
import onnxruntime
import argparse
import json

CLASSES=["target"]

class ONNX:
    def __init__(self, onnx_file_name):
        self.onnx_session = onnxruntime.InferenceSession(onnx_file_name)

    def sigmoid(self,x):
        s = 1 / (1 + np.exp(-1 * x))
        return s

    def get_result(self,class_scores):
        class_score = 0
        class_index = 0
        for i in range(len(class_scores)):
            if class_scores[i] > class_score:
                class_index += 1
                class_score = class_scores[i]
        return class_score, class_index


    def xywh2xyxy(self,x):
        # [x, y, w, h] to [x1, y1, x2, y2]
        y = np.copy(x)
        y[:, 0] = x[:, 0] - x[:, 2] / 2
        y[:, 1] = x[:, 1] - x[:, 3] / 2
        y[:, 2] = x[:, 0] + x[:, 2] / 2
        y[:, 3] = x[:, 1] + x[:, 3] / 2
        return y

    def nms(self,dets, thresh):
        x1 = dets[:, 0]
        y1 = dets[:, 1]
        x2 = dets[:, 2]
        y2 = dets[:, 3]
        areas = (y2 - y1 + 1) * (x2 - x1 + 1)
        scores = dets[:, 4]
        keep = []
        index = scores.argsort()[::-1]

        while index.size > 0:
            i = index[0]
            keep.append(i)
            x11 = np.maximum(x1[i], x1[index[1:]])
            y11 = np.maximum(y1[i], y1[index[1:]])
            x22 = np.minimum(x2[i], x2[index[1:]])
            y22 = np.minimum(y2[i], y2[index[1:]])

            w = np.maximum(0, x22 - x11 + 1)
            h = np.maximum(0, y22 - y11 + 1)

            overlaps = w * h
            ious = overlaps / (areas[i] + areas[index[1:]] - overlaps)
            idx = np.where(ious <= thresh)[0]
            index = index[idx + 1]
        return keep


    def draw(self,image, box_data):
        boxes = box_data[..., :4].astype(np.int32)
        scores = box_data[..., 4]
        classes = box_data[..., 5].astype(np.int32)
        for box, score, cl in zip(boxes, scores, classes):
            top, left, right, bottom = box
            draw = ImageDraw.Draw(image)
            draw.rectangle([(top, left), (right, bottom)],  outline ="red")
            draw.text(xy=(top, left),text='{0} {1:.2f}'.format(CLASSES[cl], score), fill=(255, 0, 0))

        return image

    def get_boxes(self, prediction, confidence_threshold=0.7, nms_threshold=0.6):
        feature_map = np.squeeze(prediction)
        conf = feature_map[..., 4] > confidence_threshold
        box = feature_map[conf == True]

        cls_cinf = box[..., 5:]
        cls = []
        for i in range(len(cls_cinf)):
            cls.append(int(np.argmax(cls_cinf[i])))
        all_cls = list(set(cls))
        output = []
        for i in range(len(all_cls)):
            curr_cls = all_cls[i]
            curr_cls_box = []
            curr_out_box = []

            for j in range(len(cls)):
                if cls[j] == curr_cls:
                    box[j][5] = curr_cls
                    curr_cls_box.append(box[j][:6])

            curr_cls_box = np.array(curr_cls_box)
            curr_cls_box = self.xywh2xyxy(curr_cls_box)
            curr_out_box = self.nms(curr_cls_box, nms_threshold)

            for k in curr_out_box:
                output.append(curr_cls_box[k])
        output = np.array(output)
        return output

    def letterbox(self, img, new_shape=(640, 640), color=(114, 114, 114), auto=False, scaleFill=False, scaleup=True, stride=32):
        shape = img.shape[:2]
        if isinstance(new_shape, int):
            new_shape = (new_shape, new_shape)

        r = min(new_shape[0] / shape[0], new_shape[1] / shape[1])
        if not scaleup:
            r = min(r, 1.0)

        ratio = r, r

        new_unpad = int(round(shape[1] * r)), int(round(shape[0] * r))
        dw, dh = new_shape[1] - new_unpad[0], new_shape[0] - new_unpad[1]

        if auto:
            dw, dh = np.mod(dw, stride), np.mod(dh, stride)
        elif scaleFill:
            dw, dh = 0.0, 0.0
            new_unpad = (new_shape[1], new_shape[0])
            ratio = new_shape[1] / shape[1], new_shape[0] / shape[0]

        dw /= 2
        dh /= 2

        if shape[::-1] != new_unpad:
            img = img.resize(new_unpad)
        top, bottom = int(round(dh - 0.1)), int(round(dh + 0.1))
        left, right = int(round(dw - 0.1)), int(round(dw + 0.1))

        img = ImageOps.expand(img, border=(left, top, right, bottom), fill=0)
        return img, ratio, (dw, dh)

    def _inference(self,image):
        org_img = image.resize((416,416))
        img = org_img.convert("RGB")
        img = np.array(img).transpose(2, 0, 1)
        img = img.astype(dtype=np.float32)
        img /= 255.0
        img = np.expand_dims(img, axis=0)

        inputs = {self.onnx_session.get_inputs()[0].name: img}
        prediction = self.onnx_session.run(None, inputs)[0]
        return prediction, org_img

    def get_distance(self,image,draw=False):
        prediction, org_img = self._inference(image)
        boxes = self.get_boxes(prediction=prediction)
        if len(boxes) == 0:
            print('No gaps were detected.')
            return 0
        else:
            if draw:
                org_img = self.draw(org_img, boxes)
                org_img.save('result.png')
            return int(boxes[..., :4].astype(np.int32)[0][0])

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='ONNX test helper from original script')
    parser.add_argument('--image', required=True, help='Image path')
    parser.add_argument('--model', default='assets/captcha.onnx', help='ONNX model path')
    args = parser.parse_args()

    try:
        onnx = ONNX(args.model)
        img = Image.open(args.image)
        print(json.dumps({"distance": onnx.get_distance(img, True)}))
    except Exception as e:
        print(json.dumps({"error": str(e)}), file=sys.stderr)
        sys.exit(1)
